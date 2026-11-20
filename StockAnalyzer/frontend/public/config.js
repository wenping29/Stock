// 前端运行时配置（部署后可直接修改此文件，无需重新构建）
// 生效方式：index.html 在 main.ts 之前加载本文件，写入 window.__APP_CONFIG__
//
// apiBase 取值说明：
//   ""                      同域部署（推荐）：nginx 托管前端并反代 /api 到后端
//   "http://1.2.3.4:5200"   前后端分离直连后端地址（需在后端 Cors:Origins 配置前端域名）
//
// 示例：window.__APP_CONFIG__ = { apiBase: "http://43.142.147.244:5200" };
//
// analysisApiBase 为量化分析接口（大盘/技术指标/回测/优化/监控）地址：
//   ""                      默认：走同源 /analysis-api，由 nginx 反代到 FundAnalysis 后端的 /api
//   "http://1.2.3.4:5000"   前后端直连后端地址（需在后端 Cors:Origins 配置前端域名）
window.__APP_CONFIG__ = {
  apiBase: "",
  analysisApiBase: "",
};
