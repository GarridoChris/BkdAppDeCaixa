# Trilha do Salão — front-end piloto

Front-end Next.js independente da API. O Next.js server-side BFF mantém o JWT em memória e entrega ao navegador apenas o identificador aleatório de sessão em cookie `HttpOnly`, `Secure` e `SameSite=Strict`. O login valida a origem; operações autenticadas que alteram dados também exigem token CSRF em cabeçalho. O BFF libera somente as rotas da API usadas nesta entrega; a API segue responsável por autorizar o papel do usuário.

## Executar no Windows / PowerShell

Na raiz do repositório, em um terminal, configure a URL da API e instale as dependências:

```powershell
cd frontend
Copy-Item .env.example .env.local
npm install
npm run dev
```

Deixe a API em execução em `http://localhost:5088` (perfil `http`) e abra `http://localhost:3000`. Se a API usar outra URL, altere `API_BASE_URL` em `frontend/.env.local`. Defina `APP_ORIGIN` para a origem pública exata quando usar outra URL.

No desenvolvimento local, os cookies `Secure` funcionam em navegadores modernos para `localhost`. Use HTTPS em qualquer ambiente que não seja local. A chave de bootstrap não é lida nem enviada pelo front-end; o cadastro inicial continua sendo uma operação administrativa separada.

## Escopo disponível

- Entrar e sair com os perfis Gerente, Caixa e Funcionário.
- Gerente e Caixa: abrir caixa, abrir comanda por nome, incluir serviço/profissional com ajuste opcional justificado, receber um pagamento integral, conferir e fechar caixa.
- Gerente: consultar comissões por profissional e quinzena, registrar pagamento via Pix ou Dinheiro e conferir o retorno e a lista de pendências. A quinzena é definida pela data de fechamento da comanda: dias 1–15 ou 16 até o último dia do mês. O cálculo usa o valor final cobrado e a porcentagem copiada do serviço, arredondando meio real para cima.
- Funcionário: consultar comissões próprias, valores pagos/pendentes e o identificador do pagamento associado.
- Todas as datas de negócio usam o fuso `America/Bahia`; valores usam `Intl.NumberFormat` com `pt-BR` e BRL sem arredondar valores recebidos.

O MVP usa armazenamento de sessão em memória, adequado a um processo único de desenvolvimento/piloto. Reiniciar o Next.js invalida as sessões. Uma implantação com múltiplas instâncias precisa de um armazenamento compartilhado de sessão antes de escalar.

## Dependências do contrato atual da API

- O token dura 15 minutos e não há endpoint de renovação nem revogação. Ao expirar ou quando a API reinicia e invalida seu JWT de desenvolvimento, o usuário precisa entrar novamente.
- Atualmente preços, valores de ordem e comissões são inteiros em reais. A interface exibe BRL com centavos (`R$ 10,00`), mas a API atual não consegue produzir nem persistir centavos para comissão. Para comissões fracionárias em centavos, o contrato e a persistência precisam ser ampliados no backend.
- O gerente recebe o recibo completo na resposta do cadastro do pagamento e consegue confirmar que os serviços saíram da lista pendente. A consulta histórica completa do pagamento não existe para gerente; o endpoint de comissão do funcionário retorna `payoutId`, data e forma de pagamento por item.
- Gerente e Caixa compartilham a lista em `GET /api/orders?status=open`. O servidor filtra pela empresa autenticada e retorna ID, cliente, caixa, abertura, estado e total. A tela atualiza a lista e a comanda selecionada ao entrar na operação, recuperar o foco, concluir uma operação e a cada 20 segundos enquanto estiver visível.
- A API ainda não fornece histórico de recibos/anexos, comprovantes ou fechamento formal da quinzena. Nenhum desses recursos é usado para registrar pagamento de comissão no fluxo atual.
- Limitação anterior do Fiado no frontend. A tela oferece essa forma de pagamento ao gerente, mas não coleta a justificativa obrigatória. A API rejeita o envio sem a nota. A liquidação de dívidas continua fora desta interface.

## Validação

Execute `npm run build` após instalar as dependências. O fluxo completo com dados reais requer API e banco ativos, um usuário de cada perfil, um profissional ligado à conta Funcionário, ao menos um serviço ativo e comandas fechadas na quinzena selecionada.

## Validação de comandas compartilhadas

Na raiz do projeto, com o PostgreSQL disponível no PATH (ou em `PG_BIN`), execute estes comandos em um terminal de desenvolvimento. O teste cria um cluster PostgreSQL temporário, aplica o esquema e inicia instâncias próprias da API e do Next em portas locais livres.

```powershell
dotnet build backend/src/AppDeCaixa.Api
npm --prefix frontend run build
node --test backend/tests/shared-orders/shared-orders.test.mjs
```

Para incluir a navegação desktop e celular, disponibilize o pacote Playwright para o Node e defina `ORDER_TEST_BROWSER` com o caminho de um executável Chromium ou Edge. Para a conferência manual, use dois usuários diferentes da mesma empresa na mesma instalação de homologação via HTTPS. Abra três comandas, alterne entre elas e lance serviços pelos dois dispositivos. Verifique o bloqueio do fechamento do caixa, pague uma comanda e confira as duas restantes. Ao finalizar as demais, confira o caixa e as comissões da quinzena.

O frontend envia `expectedOrder: { totalValue, itemCount }` no pagamento integral. Uma alteração entre a revisão e a gravação retorna HTTP 409 e exige nova conferência. Clientes anteriores da API podem continuar usando o contrato sem esse campo.
