const express = require('express')
const router = express()
const newUser = require("./User/newUser.js")

router.post('/', newUser)

module.exports = router


