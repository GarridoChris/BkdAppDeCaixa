const knex = require('../../src/connection')
const bcrypt = require('bcrypt')
const newUser = async (req, res) => {
    const firstLog = new Date  
    try {
        const {nome, email, senha, senhaConfirm} = req.body.test
        
        const novoUser = {
            
            nome,
            email,
            senha,
            senhaConfirm
        }
        
    //  const teste = await knex.insert(novoUser).into('users')

    
        
    return res.status(200).json({ success: "" })
  



    } catch (error) {
        console.log(error)
        return res.status(400).json(error.message);
    }
    
}

module.exports = newUser;