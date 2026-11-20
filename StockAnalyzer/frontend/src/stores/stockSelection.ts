import { defineStore } from 'pinia'
import type { StockInfo } from '../types/analysis'

interface StockSelectionState {
  stock: StockInfo | null
  seq: number
}

/** 跨页面的股票选中状态：股票列表 / 搜索选中后，指标、回测、优化页跟随切换 */
export const useStockSelectionStore = defineStore('stockSelection', {
  state: (): StockSelectionState => ({ stock: null, seq: 0 }),
  actions: {
    selectStock(stock: StockInfo) {
      this.stock = stock
      this.seq++
    },
  },
})