package com.lan.networkmonitor;

import android.Manifest;
import android.content.Context;
import android.content.pm.PackageManager;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.net.TransportInfo;
import android.net.wifi.ScanResult;
import android.net.wifi.WifiInfo;
import android.os.Build;
import android.telephony.CellIdentityLte;
import android.telephony.CellIdentityNr;
import android.telephony.CellInfo;
import android.telephony.CellInfoLte;
import android.telephony.CellInfoNr;
import android.telephony.CellSignalStrengthLte;
import android.telephony.CellSignalStrengthNr;
import android.telephony.SubscriptionManager;
import android.telephony.TelephonyManager;
import android.text.TextUtils;

import java.io.BufferedInputStream;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Date;
import java.util.List;
import java.util.Locale;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;

public class NetworkAnalyzer {
    private final Context context;

    public interface Callback {
        void onProgress(String text);
        void onFinished(String text);
    }

    public NetworkAnalyzer(Context context) {
        this.context = context.getApplicationContext();
    }

    public void analyze(Callback callback) {
        new Thread(() -> {
            Report r = new Report();
            try {
                callback.onProgress("正在读取当前连接…");
                collectConnectivity(r);
                callback.onProgress(r.preview("正在读取运营商与蜂窝小区…"));
                collectTelephony(r);
                callback.onProgress(r.preview("正在测试延迟与抖动…"));
                measureLatency(r);
                callback.onProgress(r.preview("正在测试下载速度…"));
                measureDownload(r);
                buildVerdict(r);
            } catch (Throwable t) {
                r.notes.add("检测异常：" + t.getClass().getSimpleName() + " " + safe(t.getMessage()));
            }
            callback.onFinished(r.render());
        }, "LanNetworkAnalyzer").start();
    }

    private void collectConnectivity(Report r) {
        ConnectivityManager cm = (ConnectivityManager) context.getSystemService(Context.CONNECTIVITY_SERVICE);
        Network network = cm.getActiveNetwork();
        if (network == null) {
            r.transport = "未连接";
            return;
        }
        NetworkCapabilities caps = cm.getNetworkCapabilities(network);
        if (caps == null) return;

        r.validated = caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_VALIDATED);
        if (caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)) {
            r.transport = "Wi‑Fi";
            TransportInfo info = caps.getTransportInfo();
            if (info instanceof WifiInfo) {
                WifiInfo wi = (WifiInfo) info;
                r.wifiRssi = wi.getRssi();
                r.wifiFreq = wi.getFrequency();
                r.wifiRx = wi.getRxLinkSpeedMbps();
                r.wifiTx = wi.getTxLinkSpeedMbps();
                String ssid = wi.getSSID();
                if (ssid != null && !"<unknown ssid>".equals(ssid)) r.ssid = ssid.replace("\"", "");
                if (Build.VERSION.SDK_INT >= 30) r.wifiStandard = wifiStandard(wi.getWifiStandard());
            }
        } else if (caps.hasTransport(NetworkCapabilities.TRANSPORT_CELLULAR)) {
            r.transport = "蜂窝数据";
        } else if (caps.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET)) {
            r.transport = "以太网";
        } else if (caps.hasTransport(NetworkCapabilities.TRANSPORT_VPN)) {
            r.transport = "VPN";
        } else {
            r.transport = "其他";
        }
    }

    private void collectTelephony(Report r) {
        if (!context.getPackageManager().hasSystemFeature(PackageManager.FEATURE_TELEPHONY)) {
            r.notes.add("设备没有蜂窝通信硬件。\n");
            return;
        }

        TelephonyManager base = (TelephonyManager) context.getSystemService(Context.TELEPHONY_SERVICE);
        TelephonyManager tm = base;
        try {
            int subId = SubscriptionManager.getDefaultDataSubscriptionId();
            if (SubscriptionManager.isValidSubscriptionId(subId)) tm = base.createForSubscriptionId(subId);
        } catch (Throwable ignored) {}

        try { r.operator = first(tm.getNetworkOperatorName(), tm.getSimOperatorName(), "未知"); }
        catch (Throwable ignored) {}

        try {
            if (context.checkSelfPermission(Manifest.permission.READ_PHONE_STATE) == PackageManager.PERMISSION_GRANTED) {
                r.dataType = networkType(tm.getDataNetworkType());
            }
        } catch (Throwable ignored) {}

        if (context.checkSelfPermission(Manifest.permission.ACCESS_FINE_LOCATION) != PackageManager.PERMISSION_GRANTED) {
            r.notes.add("未授予精确位置权限，无法读取蜂窝小区。\n");
            return;
        }

        List<CellInfo> cells = freshCells(tm);
        if (cells == null || cells.isEmpty()) {
            r.notes.add("系统没有返回 CellInfo；请确认手机系统“位置信息”总开关已打开。\n");
            return;
        }

        cells.sort((a, b) -> {
            if (a.isRegistered() != b.isRegistered()) return a.isRegistered() ? -1 : 1;
            return Integer.compare(b.getCellSignalStrength().getDbm(), a.getCellSignalStrength().getDbm());
        });

        for (CellInfo ci : cells) {
            CellRecord c = parse(ci);
            if (c != null) {
                r.cells.add(c);
                if (r.serving == null && c.registered) r.serving = c;
            }
        }
        if (r.serving == null && !r.cells.isEmpty()) r.serving = r.cells.get(0);

        for (CellRecord c : r.cells) {
            if (c.registered && "5G NR".equals(c.radio)) {
                r.dataType = "5G NR";
                break;
            }
        }
    }

    private List<CellInfo> freshCells(TelephonyManager tm) {
        final List<CellInfo>[] holder = new List[]{null};
        CountDownLatch latch = new CountDownLatch(1);
        try {
            tm.requestCellInfoUpdate(context.getMainExecutor(), new TelephonyManager.CellInfoCallback() {
                @Override public void onCellInfo(List<CellInfo> cellInfo) {
                    holder[0] = cellInfo;
                    latch.countDown();
                }
                @Override public void onError(int errorCode, Throwable detail) { latch.countDown(); }
            });
            latch.await(3500, TimeUnit.MILLISECONDS);
        } catch (Throwable ignored) {}

        if (holder[0] != null) return holder[0];
        try { return tm.getAllCellInfo(); }
        catch (Throwable e) { return Collections.emptyList(); }
    }

    private CellRecord parse(CellInfo ci) {
        try {
            if (ci instanceof CellInfoNr) {
                CellInfoNr nr = (CellInfoNr) ci;
                CellIdentityNr id = (CellIdentityNr) nr.getCellIdentity();
                CellSignalStrengthNr s = (CellSignalStrengthNr) nr.getCellSignalStrength();
                CellRecord c = new CellRecord();
                c.radio = "5G NR";
                c.registered = ci.isRegistered();
                c.dbm = s.getDbm();
                c.rsrp = s.getSsRsrp();
                c.rsrq = s.getSsRsrq();
                c.sinr = s.getSsSinr();
                c.mcc = safe(id.getMccString());
                c.mnc = safe(id.getMncString());
                c.tac = avail(id.getTac());
                c.cell = availLong(id.getNci());
                c.pci = avail(id.getPci());
                c.arfcn = avail(id.getNrarfcn());
                return c;
            }
            if (ci instanceof CellInfoLte) {
                CellInfoLte lte = (CellInfoLte) ci;
                CellIdentityLte id = lte.getCellIdentity();
                CellSignalStrengthLte s = lte.getCellSignalStrength();
                CellRecord c = new CellRecord();
                c.radio = "4G LTE";
                c.registered = ci.isRegistered();
                c.dbm = s.getDbm();
                c.rsrp = s.getRsrp();
                c.rsrq = s.getRsrq();
                c.sinr = s.getRssnr();
                c.mcc = safe(id.getMccString());
                c.mnc = safe(id.getMncString());
                c.tac = avail(id.getTac());
                c.cell = availLong(id.getCi());
                c.pci = avail(id.getPci());
                c.arfcn = avail(id.getEarfcn());
                return c;
            }
            CellRecord c = new CellRecord();
            c.radio = ci.getClass().getSimpleName().replace("CellInfo", "");
            c.registered = ci.isRegistered();
            c.dbm = ci.getCellSignalStrength().getDbm();
            return c;
        } catch (Throwable ignored) {
            return null;
        }
    }

    private void measureLatency(Report r) {
        ArrayList<Double> samples = new ArrayList<>();
        int failed = 0;
        for (int i = 0; i < 8; i++) {
            HttpURLConnection con = null;
            try {
                URL url = new URL("https://speed.cloudflare.com/__down?bytes=1&r=" + System.nanoTime());
                long start = System.nanoTime();
                con = (HttpURLConnection) url.openConnection();
                con.setConnectTimeout(3000);
                con.setReadTimeout(3000);
                con.setUseCaches(false);
                try (InputStream in = new BufferedInputStream(con.getInputStream())) { in.read(); }
                samples.add((System.nanoTime() - start) / 1_000_000.0);
            } catch (Throwable e) {
                failed++;
            } finally {
                if (con != null) con.disconnect();
            }
        }
        if (!samples.isEmpty()) {
            double delta = 0;
            for (int i = 1; i < samples.size(); i++) delta += Math.abs(samples.get(i) - samples.get(i - 1));
            r.jitter = samples.size() > 1 ? delta / (samples.size() - 1) : 0;
            ArrayList<Double> sorted = new ArrayList<>(samples);
            Collections.sort(sorted);
            r.latency = sorted.get(sorted.size() / 2);
        }
        r.failure = failed * 100.0 / 8.0;
    }

    private void measureDownload(Report r) {
        HttpURLConnection con = null;
        try {
            URL url = new URL("https://speed.cloudflare.com/__down?bytes=5000000&r=" + System.nanoTime());
            con = (HttpURLConnection) url.openConnection();
            con.setConnectTimeout(4000);
            con.setReadTimeout(5000);
            con.setUseCaches(false);
            byte[] buf = new byte[65536];
            long total = 0;
            long start = System.nanoTime();
            try (InputStream in = new BufferedInputStream(con.getInputStream())) {
                int n;
                while ((n = in.read(buf)) >= 0) total += n;
            }
            double sec = (System.nanoTime() - start) / 1_000_000_000.0;
            if (sec > 0 && total > 0) r.download = total * 8.0 / 1_000_000.0 / sec;
        } catch (Throwable e) {
            r.notes.add("下载测速失败：" + safe(e.getMessage()) + "\n");
        } finally {
            if (con != null) con.disconnect();
        }
    }

    private void buildVerdict(Report r) {
        int score = 100;
        if (!r.validated) score -= 25;
        if (r.latency < 0) score -= 20; else if (r.latency > 100) score -= 30; else if (r.latency > 70) score -= 20; else if (r.latency > 45) score -= 10;
        if (r.jitter > 25) score -= 25; else if (r.jitter > 12) score -= 15; else if (r.jitter > 6) score -= 5;
        if (r.failure >= 25) score -= 25; else if (r.failure > 0) score -= 8;
        if (r.download >= 0 && r.download < 5) score -= 15;
        if (r.serving != null && r.serving.rsrp != Integer.MIN_VALUE && r.serving.rsrp < -110) score -= 15;
        r.score = Math.max(0, Math.min(100, score));

        if (r.latency >= 0 && r.latency <= 45 && r.jitter <= 8 && r.failure == 0) {
            r.gameVerdict = "适合竞技 FPS：延迟和抖动表现良好。";
        } else if (r.latency >= 0 && r.latency <= 70 && r.jitter <= 15 && r.failure <= 12.5) {
            r.gameVerdict = "可以玩 FPS，但高峰期可能有波动。";
        } else {
            r.gameVerdict = "当前不适合竞技 FPS，优先改善延迟、抖动或蜂窝信号。";
        }
    }

    private static String wifiStandard(int s) {
        if (s == ScanResult.WIFI_STANDARD_11AX) return "Wi‑Fi 6";
        if (s == ScanResult.WIFI_STANDARD_11AC) return "Wi‑Fi 5";
        if (s == ScanResult.WIFI_STANDARD_11N) return "Wi‑Fi 4";
        return "未知";
    }

    private static String networkType(int t) {
        if (t == TelephonyManager.NETWORK_TYPE_NR) return "5G NR";
        if (t == TelephonyManager.NETWORK_TYPE_LTE) return "4G LTE";
        if (t == TelephonyManager.NETWORK_TYPE_HSPAP) return "HSPA+";
        if (t == TelephonyManager.NETWORK_TYPE_UMTS) return "3G UMTS";
        return "类型 " + t;
    }

    private static String avail(int v) { return v == CellInfo.UNAVAILABLE ? "" : String.valueOf(v); }
    private static String availLong(long v) { return v == CellInfo.UNAVAILABLE_LONG ? "" : String.valueOf(v); }
    private static String safe(String x) { return x == null ? "" : x; }
    private static String first(String... xs) { for (String x : xs) if (!TextUtils.isEmpty(x)) return x; return ""; }
    private static String fmt(double x) { return x < 0 ? "未测得" : String.format(Locale.CHINA, "%.1f", x); }
    private static String quality(int dbm) {
        if (dbm == Integer.MIN_VALUE) return "未知";
        if (dbm >= -80) return "优秀";
        if (dbm >= -95) return "良好";
        if (dbm >= -105) return "一般";
        if (dbm >= -115) return "偏弱";
        return "很弱";
    }

    private static class CellRecord {
        String radio = "", mcc = "", mnc = "", tac = "", cell = "", pci = "", arfcn = "";
        boolean registered;
        int dbm = Integer.MIN_VALUE, rsrp = Integer.MIN_VALUE, rsrq = Integer.MIN_VALUE, sinr = Integer.MIN_VALUE;
    }

    private static class Report {
        String transport = "未知", ssid = "", wifiStandard = "", operator = "未知", dataType = "未知", gameVerdict = "";
        boolean validated;
        int wifiRssi = Integer.MIN_VALUE, wifiFreq = -1, wifiRx = -1, wifiTx = -1, score;
        double latency = -1, jitter = -1, failure = 0, download = -1;
        final List<CellRecord> cells = new ArrayList<>();
        final List<String> notes = new ArrayList<>();
        CellRecord serving;

        String preview(String status) { return status + "\n\n当前连接：" + transport; }

        String render() {
            StringBuilder b = new StringBuilder();
            b.append("网络诊断报告\n").append(new SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.CHINA).format(new Date())).append("\n\n");
            b.append("综合评分  ").append(score).append(" / 100\n").append(gameVerdict).append("\n");

            b.append("\n━━━━━━━━ 当前连接 ━━━━━━━━\n");
            b.append("联网方式：").append(transport).append("\n互联网验证：").append(validated ? "通过" : "未通过").append("\n");
            if ("Wi‑Fi".equals(transport)) {
                if (!ssid.isEmpty()) b.append("SSID：").append(ssid).append("\n");
                if (!wifiStandard.isEmpty()) b.append("标准：").append(wifiStandard).append("\n");
                if (wifiFreq > 0) b.append("频段：").append(wifiFreq >= 4900 ? "5 GHz" : "2.4 GHz").append(" (").append(wifiFreq).append(" MHz)\n");
                if (wifiRssi != Integer.MIN_VALUE) b.append("Wi‑Fi RSSI：").append(wifiRssi).append(" dBm  ").append(quality(wifiRssi)).append("\n");
                if (wifiRx >= 0) b.append("接收链路：").append(wifiRx).append(" Mbps\n");
                if (wifiTx >= 0) b.append("发送链路：").append(wifiTx).append(" Mbps\n");
            }

            b.append("\n━━━━━━━━ 实际网络 ━━━━━━━━\n");
            b.append("HTTPS 延迟：").append(fmt(latency)).append(" ms\n");
            b.append("延迟抖动：").append(fmt(jitter)).append(" ms\n");
            b.append("探测失败率：").append(fmt(failure)).append(" %\n");
            b.append("下载速度：").append(fmt(download)).append(" Mbps\n");

            b.append("\n━━━━━━━━ 蜂窝网络 ━━━━━━━━\n");
            b.append("运营商：").append(operator).append("\n数据网络：").append(dataType).append("\n");
            if (serving != null) {
                b.append("当前服务小区：").append(serving.radio).append("\n");
                b.append("信号：").append(serving.dbm).append(" dBm  ").append(quality(serving.dbm)).append("\n");
                if (serving.rsrp != Integer.MIN_VALUE) b.append("RSRP：").append(serving.rsrp).append(" dBm\n");
                if (serving.rsrq != Integer.MIN_VALUE) b.append("RSRQ：").append(serving.rsrq).append(" dB\n");
                if (serving.sinr != Integer.MIN_VALUE) b.append("SINR/SNR：").append(serving.sinr).append(" dB\n");
                if (!serving.mcc.isEmpty() || !serving.mnc.isEmpty()) b.append("MCC/MNC：").append(serving.mcc).append("/").append(serving.mnc).append("\n");
                if (!serving.tac.isEmpty()) b.append("TAC：").append(serving.tac).append("\n");
                if (!serving.cell.isEmpty()) b.append("Cell ID / NCI：").append(serving.cell).append("\n");
                if (!serving.pci.isEmpty()) b.append("PCI：").append(serving.pci).append("\n");
                if (!serving.arfcn.isEmpty()) b.append("ARFCN：").append(serving.arfcn).append("\n");
            } else b.append("没有读取到服务小区。\n");

            if (!cells.isEmpty()) {
                b.append("\n系统可见小区（最多 8 个）：\n");
                for (int i = 0; i < Math.min(8, cells.size()); i++) {
                    CellRecord c = cells.get(i);
                    b.append(c.registered ? "★ " : "  ").append(c.radio).append("  ").append(c.dbm).append(" dBm");
                    if (!c.pci.isEmpty()) b.append("  PCI ").append(c.pci);
                    b.append("\n");
                }
            }

            b.append("\n━━━━━━━━ 基站说明 ━━━━━━━━\n");
            b.append("Android 能读取当前服务小区和邻区标识，但系统不会直接提供铁塔经纬度。当前服务小区也不一定是物理距离最近的铁塔。后续可接入可信基站数据库做坐标反查。\n");
            b.append("\n━━━━━━━━ FPS 建议 ━━━━━━━━\n");
            b.append("《无畏契约 / 三角洲行动》优先看延迟、抖动和失败率。连接随身 Wi‑Fi 时优先使用 5 GHz，并在晚高峰再次测试以判断基站拥塞。\n");
            if (!notes.isEmpty()) {
                b.append("\n━━━━━━━━ 注意 ━━━━━━━━\n");
                for (String n : notes) b.append("• ").append(n);
            }
            b.append("\n测速端点：Cloudflare Speed Test");
            return b.toString();
        }
    }
}
