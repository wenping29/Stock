#!/usr/bin/env bash
# deploy-collector.sh - 采集器打包 + 部署脚本（参照 pack.sh / upload.sh）
# 用法：bash deploy/deploy-collector.sh
#   1) 发布 FundAnalysis.Collector（linux-x64 自包含）
#   2) 交互式选择全量/增量上传到服务器
#   3) 以 www 用户 daemon 模式重启采集器并做启动检查
set -euo pipefail

# ==================== 配置区 ====================
SERVER="root@43.142.147.244"
PORT="22"
SSH_KEY="$HOME/.ssh/tx.pem"

LOCAL_COLLECTOR=""   # 由下方 ROOT_LOCAL 推导
REMOTE_COLLECTOR="/www/wwwroot/fund/collectter"   # 服务器上既有目录名就是 collectter
EXECUTABLE_NAME="FundAnalysis.Collector"
CORE_NAME="FundAnalysis"                          # 共享核心程序集名为 FundAnalysis.Core.dll，不是 FundAnalysis.Collector.Core.dll
START_ARGS="--mode=daemon"                        # 常驻模式，内部按 cron 表自调度
# ===============================================

ROOT_LOCAL="$(cd "$(dirname "$0")/.." && pwd)"
LOCAL_COLLECTOR="$ROOT_LOCAL/deploy/dist/collector/"

echo "== 发布采集器（linux-x64 自包含） =="
rm -rf "$LOCAL_COLLECTOR"
dotnet publish "$ROOT_LOCAL/backend/FundAnalysis.Collector/FundAnalysis.Collector.csproj" \
  -c Release -r linux-x64 --self-contained true -o "$LOCAL_COLLECTOR"
echo "发布完成：$LOCAL_COLLECTOR"

read -p "是否首次部署或升级了 .NET 环境依赖？(输入 y 全量上传，直接回车默认增量上传): " IS_FULL_DEPLOY
echo ""

if [ ! -f "$LOCAL_COLLECTOR$EXECUTABLE_NAME" ]; then
  echo "错误: 本地发布产物不存在 -> $LOCAL_COLLECTOR$EXECUTABLE_NAME"
  exit 1
fi

# ==================== 1. 停止旧进程 ====================
echo "========== 停止旧采集器进程 =========="
ssh -i "$SSH_KEY" -p "$PORT" "$SERVER" bash -s << EOF
    pkill -f "$EXECUTABLE_NAME" || true
    sleep 2
    if pgrep -f "$EXECUTABLE_NAME" >/dev/null; then
      echo "旧采集器进程仍未停止，已中止部署（避免覆盖运行中程序的文件）" >&2
      exit 1
    fi
    echo "旧进程已停止"
    mkdir -p "$REMOTE_COLLECTOR"
EOF

# ==================== 2. 上传 ====================
echo "========== 上传采集器 =========="
if [[ "$IS_FULL_DEPLOY" == "y" || "$IS_FULL_DEPLOY" == "Y" ]]; then
  echo ">>> 模式：全量上传（包含运行时依赖，保留服务器 appsettings.json）..."
  tar -czf - -C "$LOCAL_COLLECTOR" --exclude=appsettings.json . \
    | ssh -i "$SSH_KEY" -p "$PORT" "$SERVER" "tar -xzf - -C '$REMOTE_COLLECTOR'"
else
  echo ">>> 模式：增量上传（仅核心 DLL 与可执行文件）..."
  echo "⚠️  自包含部署下，SDK/运行时或第三方包升级后必须选全量上传，否则可能报 BadImageFormatException（Bad IL format）"
  scp -i "$SSH_KEY" -P "$PORT" \
    "$LOCAL_COLLECTOR$EXECUTABLE_NAME" \
    "$LOCAL_COLLECTOR$EXECUTABLE_NAME.dll" \
    "$LOCAL_COLLECTOR$EXECUTABLE_NAME.deps.json" \
    "$LOCAL_COLLECTOR$EXECUTABLE_NAME.runtimeconfig.json" \
    "$LOCAL_COLLECTOR$EXECUTABLE_NAME.pdb" \
    "$LOCAL_COLLECTOR$CORE_NAME.Core.dll" \
    "$LOCAL_COLLECTOR$CORE_NAME.Core.pdb" \
    "$LOCAL_COLLECTOR$CORE_NAME.Core.xml" \
    "$SERVER:$REMOTE_COLLECTOR/" || echo "提示：部分文件（如 .pdb 或 .xml）可能不存在，已跳过。"
fi
echo "采集器上传完成"

# ==================== 3. 启动 ====================
echo "========== 启动采集器 =========="
ssh -i "$SSH_KEY" -p "$PORT" "$SERVER" bash -s << EOF
    chown -R www:www "$REMOTE_COLLECTOR"
    rm -f "$REMOTE_COLLECTOR/app.log"

    cd "$REMOTE_COLLECTOR"
    chmod +x "$EXECUTABLE_NAME"
    su -s /bin/bash www -c "nohup ./$EXECUTABLE_NAME $START_ARGS > app.log 2>&1 &"

    sleep 3
    echo "--- 进程 ---"
    ps -ef | grep "$EXECUTABLE_NAME" | grep -v grep | head -3 || true
    echo "--- 启动日志（最后 10 行） ---"
    tail -n 10 "$REMOTE_COLLECTOR/app.log" 2>/dev/null || true
EOF

echo "========== 采集器部署完成 =========="
