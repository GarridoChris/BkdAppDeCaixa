
const db = require('knex')
const knex = db({
  client: 'pg',
  connection: {
    
    host:'localhost',
    port: 5432,
    user: 'postgres',
    database: 'postgres',
    password: process.env.PGPASSWORD
    
  },
});


module.exports = knex