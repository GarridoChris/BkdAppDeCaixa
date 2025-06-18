// const router = require('routes/router.js') 
const express = require('express')
const router = require('../routes/router')
const app = express()

app.use(express.json())

app.use(router)
const port = 3000
app.listen(port, () => {
  console.log(`Example app listening on port ${port}`)
})
