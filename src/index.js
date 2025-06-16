// const router = require('routes/router.js') 
const express = require('express')
const app = express()

app.get('/',(req, res)=>{
  res.send("okfoi hehe")
})
app.get('/teste2',(req, res)=>{
  res.send("okfoi hehe")
})

const port = 3000

app.listen(port, () => {
  console.log(`Example app listening on port ${port}`)
})
