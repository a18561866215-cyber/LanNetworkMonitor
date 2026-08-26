# 岚 · 网络体检（Android）

一个极简的单页面 Android 网络诊断 App。

## 当前版本 v0.1

点击唯一按钮后自动输出：

- 当前网络类型（Wi-Fi / 蜂窝 / 以太网 / VPN）
- Wi-Fi 频段（2.4 / 5 GHz）、标准、RSSI、收发链路速率
- 运营商与数据网络类型
- 4G LTE / 5G NR 当前服务小区、邻区
- MCC / MNC / TAC / Cell ID(NCI) / PCI / ARFCN
- 5G SS-RSRP / SS-RSRQ / SS-SINR（系统有上报时）
- HTTPS 延迟、抖动、探测失败率
- Cloudflare 下载 / 上传测速
- 面向《无畏契约》《三角洲行动》的网络稳定性结论

## 重要边界

1. Android 的 `TelephonyManager` 可以提供当前服务小区和邻近小区，但系统本身不直接提供铁塔经纬度。
2. 项目内预留了 OpenCellID 查询：打开 `NetworkAnalyzer.java`，把 `OPEN_CELL_ID_KEY` 填入即可尝试把服务小区反查成坐标。
3. OpenCellID 的坐标可能是众包测量计算结果，不应理解为运营商官方铁塔坐标。
4. 如果手机连的是“随身 Wi-Fi”，本 App 能分析手机到热点的 Wi-Fi 链路以及最终互联网质量，但无法越过热点读取热点内部 5G Modem 的 CellInfo。
5. “探测失败率”是 HTTPS 探测失败比例，不等同于底层 ICMP 真正 packet loss；普通 Android App 无 root 权限时这样做兼容性更好。

## 自动构建 APK

仓库已配置 GitHub Actions。每次推送到 `main` 后，GitHub 会自动构建 Debug APK，并把它作为名为 `LanNetworkMonitor-APK` 的构建产物上传。

## OpenCellID

注册地址：`https://opencellid.org`

API Key 不应该上传到公开 GitHub 仓库。如果后续加入真实 API Key，建议先将仓库切换为 Private，并优先使用安全配置或后端代理。