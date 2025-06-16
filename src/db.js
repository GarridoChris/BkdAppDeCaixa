const dotenv = requires("dotenv")
const knex = require('knex')({
  client: 'postgres',
  connection: {
    host: dotenv.DB_HOST,
    port: dotenv.DB_PORT,
    user: dotenv.DB_USER,
    password: dotenv.DB_PASS,
    database: dotenv.DB_NAME,
  },
});

module.exports = {
    knex
}