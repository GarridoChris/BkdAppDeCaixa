
const db = require('knex')
const knex = db({
  client: 'pg',
  connection: {
    
    host:'localhost',
    port: 5432,
    user: 'postgres',
    database: 'postgres',
    password: '6213284'
    
  },
});


module.exports = knex