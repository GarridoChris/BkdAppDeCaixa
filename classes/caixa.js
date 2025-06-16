class Register {
  #openTime = null;
  #entries = [];
  #outtakes = [];
  #tips = [];

  constructor(initialBalance = 0) {
    this.initialBalance = initialBalance;
  }

  openRegister() {
    if (this.#openTime !== null) {
      throw new Error("Register is already open.");
    }
    this.#openTime = new Date();
  }

  closeRegister() {
    if (this.#openTime === null) {
      throw new Error("Register is not open.");
    }
    this.#openTime = null;
  }

  addEntry(amount) {
    if (amount <= 0) throw new Error("Entry amount must be positive.");
    this.#entries.push(amount);
  }

  addOuttake(amount) {
    if (amount <= 0) throw new Error("Outtake amount must be positive.");
    this.#outtakes.push(amount);
  }

  addTip(amount) {
    if (amount <= 0) throw new Error("Tip amount must be positive.");
    this.#tips.push(amount);
  }

  getBalance() {
    const totalEntries = this.#entries.reduce((a, b) => a + b, 0);
    const totalOuttakes = this.#outtakes.reduce((a, b) => a + b, 0);
    const totalTips = this.#tips.reduce((a, b) => a + b, 0);
    return this.initialBalance + totalEntries - totalOuttakes + totalTips;
  }

  getReport() {
    return {
      openedAt: this.#openTime,
      entries: this.#entries,
      outtakes: this.#outtakes,
      tips: this.#tips,
      currentBalance: this.getBalance()
    };
  }
}
