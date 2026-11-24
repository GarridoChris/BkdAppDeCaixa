import { execFileSync, spawn } from "node:child_process";
import { randomBytes } from "node:crypto";
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { createConnection, createServer } from "node:net";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { setTimeout as delay } from "node:timers/promises";

async function availablePort() {
  const socket = createServer(peer => peer.end());
  await new Promise((ok, fail) => socket.once("error", fail).listen(0, "127.0.0.1", ok));
  const port = socket.address().port;
  try {
    await new Promise((ok, fail) => {
      const client = createConnection(port, "127.0.0.1");
      client.once("connect", () => { client.end(); ok(); });
      client.once("error", fail);
      client.setTimeout(3000, () => client.destroy(Error("Loopback connection timed out")));
    });
  } finally {
    await new Promise(ok => socket.close(ok));
  }
  return port;
}

// Always create our own cluster. Never accept a connection string or an existing database.
export async function createFixture(t) {
  const root = resolve(import.meta.dirname, "../../..");
  const frontend = join(root, "frontend");
  const apiDll = join(root, "backend/src/AppDeCaixa.Api/bin/Debug/net10.0/AppDeCaixa.Api.dll");
  if (!existsSync(apiDll) || !existsSync(join(frontend, ".next/BUILD_ID"))) {
    throw Error("Build the API (Debug) and the frontend before running the shared orders tests.");
  }
  const pgPort = await availablePort();
  const dir = mkdtempSync(join(tmpdir(), "shared-orders-"));
  const data = join(dir, "data");
  const pgLog = join(dir, "postgres.log");
  const password = randomBytes(24).toString("hex");
  const env = { ...process.env, PGPASSWORD: password, PGCLIENTENCODING: "UTF8" };
  const pg = (tool, args, opts) => execFileSync(join(process.env.PG_BIN || "", tool), args, {
    env, encoding: "utf8", windowsHide: true, timeout: 180000, ...opts,
  });
  let pgStarted = false;
  const servers = [];
  t.after(async () => {
    for (const child of servers.reverse()) {
      if (child.exitCode === null && child.signalCode === null) {
        await new Promise(ok => { child.once("exit", ok); child.kill(); });
      }
    }
    if (pgStarted) pg("pg_ctl", ["-D", data, "-m", "fast", "-w", "stop"]);
    rmSync(dir, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
  });

  writeFileSync(join(dir, "pw"), password, { mode: 0o600 });
  pg("initdb", ["-D", data, "-U", "order_test", "--auth=scram-sha-256", "--pwfile", join(dir, "pw"),
    "--locale=C", "--encoding=UTF8", "--no-sync"]);
  try {
    pg("pg_ctl", ["-D", data, "-l", pgLog, "-o", "-h 127.0.0.1 -p " + pgPort, "-w", "start"], { stdio: "ignore" });
    pgStarted = true;
  } catch (error) {
    throw Error(existsSync(pgLog) ? readFileSync(pgLog, "utf8") : error.message);
  }
  const dbEnv = { ...env, ASPNETCORE_ENVIRONMENT: "Development", DOTNET_ENVIRONMENT: "Development",
    Bootstrap__AdminKey: password, APPDECAIXA_SEED_DEMO: "false",
    ConnectionStrings__Default: "Host=127.0.0.1;Port=" + pgPort + ";Database=postgres;Username=order_test;Password=" + password };
  execFileSync("dotnet", [apiDll, "--migrate"], { env: dbEnv, windowsHide: true, timeout: 90000, stdio: ["ignore", "pipe", "inherit"] });
  async function serve(command, args, cwd, environment, probe) {
    const child = spawn(command, args, { cwd, env: environment, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
    servers.push(child);
    let log = "";
    const append = chunk => { log = (log + chunk).slice(-4000); };
    child.stdout.on("data", append);
    child.stderr.on("data", append);
    for (let i = 0; i < 160 && child.exitCode === null; i++) {
      try { if ((await fetch(probe)).ok) return; } catch { /* starting */ }
      await delay(250);
    }
    throw Error("Could not start test server: " + log);
  }

  const api = "http://127.0.0.1:" + await availablePort();
  await serve("dotnet", [apiDll, "--urls", api], root, dbEnv, api + "/health");
  const port = await availablePort();
  const web = "http://127.0.0.1:" + port;
  await serve(process.execPath, ["node_modules/next/dist/bin/next", "start", "-H", "127.0.0.1", "-p", String(port)],
    frontend, { ...env, API_BASE_URL: api, APP_ORIGIN: web }, web);
  const setup = await fetch(api + "/api/auth/bootstrap-company-manager", {
    method: "POST", headers: { "Content-Type": "application/json", "X-Admin-Bootstrap-Key": password },
    body: JSON.stringify({ companyName: "Test A", cnpj: "00000000000001", contactName: "Test manager",
      managerName: "Manager A", managerEmail: "manager@example.test", managerPassword: password }),
  });
  if (setup.status !== 201) throw Error("Bootstrap: " + await setup.text());
  const sql = query => pg("psql", ["-X", "-v", "ON_ERROR_STOP=1", "-h", "127.0.0.1", "-p", String(pgPort),
    "-U", "order_test", "-d", "postgres", "-f", "-"], { input: query });
  sql(`
    INSERT INTO empresa (nome,cnpj,ponto_focal) VALUES ('Test B','00000000000002','Test');
    INSERT INTO colaborador (empresa_id,nome,ativo) SELECT empresa_id,'Professional',true FROM empresa WHERE nome='Test A';
    INSERT INTO usuario (empresa_id,nome,email,senha_hash,ativo,role,colaborador_id)
      SELECT e.empresa_id, u.role, u.email, m.senha_hash, true, u.role,
        CASE WHEN u.role='Employee' THEN (SELECT colaborador_id FROM colaborador) END
      FROM usuario m CROSS JOIN (VALUES
        ('Test A','cashier@example.test','Cashier'), ('Test A','employee@example.test','Employee'),
        ('Test B','other@example.test','Manager')) AS u(company,email,role)
      JOIN empresa e ON e.nome=u.company WHERE m.role='Manager';
    INSERT INTO servicos (empresa_id,nome,descricao,porcentagem,valor,ativo)
      SELECT empresa_id,s.name,s.name,50,s.price,true FROM empresa CROSS JOIN
        (VALUES ('Corte',31),('Escova',40),('Tratamento',55)) s(name,price) WHERE nome='Test A';
  `);
  return { web, password, sql };
}
