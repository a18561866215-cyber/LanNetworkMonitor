package com.arashi.lunwu;

import android.app.Activity;
import android.app.AppOpsManager;
import android.app.usage.UsageStats;
import android.app.usage.UsageStatsManager;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ApplicationInfo;
import android.content.pm.PackageManager;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.os.Process;
import android.provider.Settings;
import android.view.Gravity;
import android.view.View;
import android.view.Window;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import android.widget.Toast;

import java.text.DateFormat;
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

public class LauncherActivity extends Activity {
    private static final int BG = Color.rgb(13, 14, 11);
    private static final int CARD = Color.rgb(28, 30, 23);
    private static final int TEXT = Color.rgb(245, 241, 221);
    private static final int MUTED = Color.rgb(164, 166, 145);
    private static final int YELLOW = Color.rgb(245, 216, 76);
    private static final int GREEN = Color.rgb(178, 243, 178);

    private TextView status;
    private TextView recentTitle;
    private LinearLayout recentBox;
    private Button usageButton;

    @Override protected void onCreate(Bundle state) {
        super.onCreate(state);
        Window w = getWindow();
        w.setStatusBarColor(BG);
        w.setNavigationBarColor(BG);
        w.addFlags(WindowManager.LayoutParams.FLAG_DRAWS_SYSTEM_BAR_BACKGROUNDS);
        setContentView(buildUi());
        refreshState();
        if (hasUsageAccess()) scanRecent();
    }

    @Override protected void onResume() {
        super.onResume();
        if (status != null) refreshState();
    }

    private View buildUi() {
        FrameLayout root = new FrameLayout(this);
        root.setBackgroundColor(BG);
        root.addView(new BananaBackground(this), new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT));

        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        root.addView(scroll, new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT));

        LinearLayout body = new LinearLayout(this);
        body.setOrientation(LinearLayout.VERTICAL);
        body.setPadding(dp(20), dp(24), dp(20), dp(36));
        scroll.addView(body);

        body.addView(text("轮·舞", 28, TEXT, true));
        body.addView(text("GAME SESSION PREP · LIGHT / DEEP", 9, MUTED, true), top(2));

        DeviceAdapter.Profile profile = DeviceAdapter.detect();
        TextView adapter = text("适配层 · " + profile.name + " · 自动回退 Standard Android", 11, YELLOW, true);
        body.addView(adapter, top(14));

        body.addView(buildLightCard(), top(16));
        body.addView(buildDeepCard(), top(14));

        recentTitle = text("最近活跃应用", 15, TEXT, true);
        body.addView(recentTitle, top(18));
        recentBox = new LinearLayout(this);
        recentBox.setOrientation(LinearLayout.VERTICAL);
        body.addView(recentBox, top(10));
        showEmpty("等待轻清理分析…");
        return root;
    }

    private View buildLightCard() {
        LinearLayout card = card();
        card.addView(text("轻清理 · 默认", 18, TEXT, true));
        status = text("", 11, MUTED, false);
        card.addView(status, top(8));

        usageButton = button("分析最近活动", true);
        usageButton.setOnClickListener(v -> {
            if (!hasUsageAccess()) {
                try { startActivity(new Intent(Settings.ACTION_USAGE_ACCESS_SETTINGS)); }
                catch (Throwable ignored) {}
            } else scanRecent();
        });
        card.addView(usageButton, top(14));

        Button optimize = button("打开系统优化 / 内存清理", true);
        optimize.setOnClickListener(v -> DeviceAdapter.openOptimizer(this));
        card.addView(optimize, top(8));

        Button game = button("打开厂商游戏模式", false);
        game.setOnClickListener(v -> {
            DeviceAdapter.openGameMode(this);
            Toast.makeText(this, "若厂商没有独立入口，将回退到系统优化", Toast.LENGTH_SHORT).show();
        });
        card.addView(game, top(8));

        TextView note = text("不需要 Shizuku。轻清理只调用系统允许的入口，不伪装成“强杀进程”。", 10, MUTED, false);
        card.addView(note, top(12));
        return card;
    }

    private View buildDeepCard() {
        LinearLayout card = card();
        card.addView(text("深度模式 · 可选", 18, TEXT, true));
        card.addView(text("需要时再进入原来的 Shizuku force-stop 模式；平时完全可以不用。", 11, MUTED, false), top(8));

        Button deep = button("进入深度清理", false);
        deep.setOnClickListener(v -> {
            try { startActivity(new Intent(this, MainActivity.class)); }
            catch (Throwable t) { Toast.makeText(this, "深度模式暂不可用", Toast.LENGTH_SHORT).show(); }
        });
        card.addView(deep, top(14));
        return card;
    }

    private void refreshState() {
        if (hasUsageAccess()) {
            status.setText("轻清理已就绪 · 可分析最近使用过的第三方应用");
            status.setTextColor(GREEN);
            usageButton.setText("重新分析最近活动");
        } else {
            status.setText("需要一次“使用情况访问”权限，才能判断最近活跃应用");
            status.setTextColor(YELLOW);
            usageButton.setText("授予使用情况访问");
        }
    }

    private boolean hasUsageAccess() {
        try {
            AppOpsManager ops = (AppOpsManager) getSystemService(APP_OPS_SERVICE);
            int mode = ops.checkOpNoThrow(AppOpsManager.OPSTR_GET_USAGE_STATS, Process.myUid(), getPackageName());
            return mode == AppOpsManager.MODE_ALLOWED;
        } catch (Throwable t) {
            return false;
        }
    }

    private void scanRecent() {
        if (!hasUsageAccess()) return;
        recentTitle.setText("最近活跃应用 · 轻清理分析");
        showEmpty("正在分析最近 6 小时的使用记录…");
        new Thread(() -> {
            List<AppInfo> apps = collectRecentApps();
            runOnUiThread(() -> renderApps(apps));
        }, "lunwu-light-scan").start();
    }

    private List<AppInfo> collectRecentApps() {
        long now = System.currentTimeMillis();
        long since = now - 6L * 60L * 60L * 1000L;
        UsageStatsManager usm = (UsageStatsManager) getSystemService(USAGE_STATS_SERVICE);
        List<UsageStats> stats = usm == null ? null : usm.queryUsageStats(UsageStatsManager.INTERVAL_DAILY, since, now);
        if (stats == null) return new ArrayList<>();

        Map<String, Long> latest = new HashMap<>();
        for (UsageStats u : stats) {
            if (u == null || u.getLastTimeUsed() <= since) continue;
            Long old = latest.get(u.getPackageName());
            if (old == null || u.getLastTimeUsed() > old) latest.put(u.getPackageName(), u.getLastTimeUsed());
        }

        PackageManager pm = getPackageManager();
        List<AppInfo> out = new ArrayList<>();
        for (Map.Entry<String, Long> e : latest.entrySet()) {
            String pkg = e.getKey();
            if (pkg.equals(getPackageName()) || pkg.equals("moe.shizuku.privileged.api")) continue;
            try {
                ApplicationInfo ai = pm.getApplicationInfo(pkg, 0);
                if ((ai.flags & ApplicationInfo.FLAG_SYSTEM) != 0 ||
                        (ai.flags & ApplicationInfo.FLAG_UPDATED_SYSTEM_APP) != 0) continue;
                if (pm.getLaunchIntentForPackage(pkg) == null) continue;
                out.add(new AppInfo(String.valueOf(pm.getApplicationLabel(ai)), pkg, e.getValue()));
            } catch (Throwable ignored) {}
        }
        Collections.sort(out, (a, b) -> Long.compare(b.lastUsed, a.lastUsed));
        if (out.size() > 24) return new ArrayList<>(out.subList(0, 24));
        return out;
    }

    private void renderApps(List<AppInfo> apps) {
        recentBox.removeAllViews();
        if (apps.isEmpty()) {
            showEmpty("没有找到近期活跃的第三方应用");
            status.setText("当前没有明显需要处理的近期活跃应用");
            status.setTextColor(MUTED);
            return;
        }

        DateFormat tf = android.text.format.DateFormat.getTimeFormat(this);
        for (AppInfo app : apps) {
            LinearLayout item = new LinearLayout(this);
            item.setOrientation(LinearLayout.VERTICAL);
            item.setPadding(dp(14), dp(12), dp(14), dp(12));
            item.setBackground(round(Color.rgb(24, 26, 20), 20));

            item.addView(text(app.label, 13, TEXT, true));
            TextView meta = text("最近使用 " + tf.format(app.lastUsed) + " · " + app.pkg, 9, MUTED, false);
            item.addView(meta, top(4));

            LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
            p.bottomMargin = dp(8);
            recentBox.addView(item, p);
        }
        status.setText("发现 " + apps.size() + " 个近期活跃应用 · 可先进入系统优化再启动游戏");
        status.setTextColor(GREEN);
    }

    private void showEmpty(String s) {
        recentBox.removeAllViews();
        TextView t = text(s, 11, MUTED, false);
        t.setGravity(Gravity.CENTER);
        t.setPadding(dp(10), dp(22), dp(10), dp(22));
        t.setBackground(round(Color.rgb(24, 26, 20), 20));
        recentBox.addView(t);
    }

    private LinearLayout card() {
        LinearLayout v = new LinearLayout(this);
        v.setOrientation(LinearLayout.VERTICAL);
        v.setPadding(dp(18), dp(18), dp(18), dp(18));
        v.setBackground(round(CARD, 24));
        return v;
    }

    private TextView text(String s, float sp, int color, boolean bold) {
        TextView t = new TextView(this);
        t.setText(s);
        t.setTextSize(sp);
        t.setTextColor(color);
        t.setTypeface(android.graphics.Typeface.create("sans", bold
                ? android.graphics.Typeface.BOLD : android.graphics.Typeface.NORMAL));
        return t;
    }

    private Button button(String s, boolean primary) {
        Button b = new Button(this);
        b.setText(s);
        b.setTextSize(12);
        b.setAllCaps(false);
        b.setMinHeight(0);
        b.setPadding(dp(14), 0, dp(14), 0);
        b.setTextColor(primary ? Color.rgb(22, 22, 16) : TEXT);
        b.setBackground(round(primary ? YELLOW : Color.rgb(48, 50, 39), 18));
        return b;
    }

    private GradientDrawable round(int color, float radiusDp) {
        GradientDrawable g = new GradientDrawable();
        g.setColor(color);
        g.setCornerRadius(dp(radiusDp));
        return g;
    }

    private LinearLayout.LayoutParams top(int value) {
        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        p.topMargin = dp(value);
        return p;
    }

    private int dp(float v) {
        return Math.round(v * getResources().getDisplayMetrics().density);
    }

    static final class AppInfo {
        final String label, pkg;
        final long lastUsed;
        AppInfo(String label, String pkg, long lastUsed) {
            this.label = label;
            this.pkg = pkg;
            this.lastUsed = lastUsed;
        }
    }

    static final class BananaBackground extends View {
        private final Paint fill = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Paint stroke = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final float d;

        BananaBackground(Context c) {
            super(c);
            d = getResources().getDisplayMetrics().density;
            stroke.setStyle(Paint.Style.STROKE);
            stroke.setStrokeWidth(1.1f * d);
        }

        @Override protected void onDraw(Canvas c) {
            super.onDraw(c);
            c.drawColor(BG);
            float w = getWidth(), h = getHeight();
            float[][] q = {{.08f,.15f,-20,.85f},{.75f,.21f,24,.70f},{.14f,.48f,18,.65f},
                    {.80f,.56f,-30,.90f},{.05f,.81f,30,.75f},{.68f,.88f,10,.60f}};
            for (float[] a : q) banana(c, w * a[0], h * a[1], a[2], a[3]);
        }

        private void banana(Canvas c, float x, float y, float rot, float scale) {
            c.save();
            c.translate(x, y);
            c.rotate(rot);
            c.scale(scale, scale);
            Path path = new Path();
            path.moveTo(2*d, 16*d);
            path.cubicTo(35*d, 48*d, 89*d, 45*d, 116*d, 8*d);
            path.cubicTo(92*d, 64*d, 32*d, 74*d, -4*d, 29*d);
            path.close();
            fill.setStyle(Paint.Style.FILL);
            fill.setColor(Color.argb(18, 245, 216, 76));
            c.drawPath(path, fill);
            stroke.setColor(Color.argb(28, 255, 235, 122));
            c.drawPath(path, stroke);
            c.restore();
        }
    }
}
