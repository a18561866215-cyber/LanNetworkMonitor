package com.lan.networkmonitor;

import android.Manifest;
import android.app.Activity;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.view.Gravity;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

public class MainActivity extends Activity {
    private static final int REQ = 1001;
    private Button button;
    private TextView report;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        buildUi();
    }

    private void buildUi() {
        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        scroll.setBackgroundColor(Color.rgb(11, 13, 16));

        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(dp(22), dp(36), dp(22), dp(36));
        root.setGravity(Gravity.CENTER_HORIZONTAL);
        scroll.addView(root, new ScrollView.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        TextView title = new TextView(this);
        title.setText("岚 · 网络体检");
        title.setTextColor(Color.WHITE);
        title.setTextSize(26);
        title.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        root.addView(title, fullWrap());

        TextView subtitle = new TextView(this);
        subtitle.setText("一键分析网络质量、5G/4G 小区和游戏稳定性");
        subtitle.setTextColor(Color.rgb(145, 151, 161));
        subtitle.setTextSize(14);
        LinearLayout.LayoutParams subLp = fullWrap();
        subLp.topMargin = dp(8);
        subLp.bottomMargin = dp(28);
        root.addView(subtitle, subLp);

        button = new Button(this);
        button.setText("开始分析网络");
        button.setTextSize(17);
        button.setTextColor(Color.rgb(6, 17, 21));
        button.setAllCaps(false);
        button.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(Color.rgb(157, 235, 255));
        bg.setCornerRadius(dp(18));
        button.setBackground(bg);
        button.setOnClickListener(v -> start());
        LinearLayout.LayoutParams bLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(58));
        bLp.bottomMargin = dp(24);
        root.addView(button, bLp);

        report = new TextView(this);
        report.setText("点击上方按钮开始。\n\n首次检测需要精确位置和电话权限，用于读取 Android 提供的 4G/5G 小区与运营商信息。");
        report.setTextColor(Color.rgb(214, 220, 226));
        report.setTextSize(14);
        report.setLineSpacing(dp(3), 1f);
        report.setTextIsSelectable(true);
        report.setTypeface(Typeface.MONOSPACE);
        root.addView(report, fullWrap());

        setContentView(scroll);
    }

    private void start() {
        if (checkSelfPermission(Manifest.permission.ACCESS_FINE_LOCATION) != PackageManager.PERMISSION_GRANTED ||
                checkSelfPermission(Manifest.permission.READ_PHONE_STATE) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[]{Manifest.permission.ACCESS_FINE_LOCATION, Manifest.permission.READ_PHONE_STATE}, REQ);
            return;
        }
        runAnalysis();
    }

    private void runAnalysis() {
        button.setEnabled(false);
        button.setText("正在分析…");
        report.setText("正在初始化网络检测…");
        new NetworkAnalyzer(this).analyze(new NetworkAnalyzer.Callback() {
            @Override public void onProgress(String text) { runOnUiThread(() -> report.setText(text)); }
            @Override public void onFinished(String text) {
                runOnUiThread(() -> {
                    report.setText(text);
                    button.setEnabled(true);
                    button.setText("重新分析");
                });
            }
        });
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == REQ && checkSelfPermission(Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED) {
            runAnalysis();
        } else if (requestCode == REQ) {
            report.setText("没有获得精确位置权限，因此无法读取蜂窝小区信息。你可以重新点击按钮并授权后再试。");
        }
    }

    private LinearLayout.LayoutParams fullWrap() {
        return new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
    }

    private int dp(int v) { return Math.round(v * getResources().getDisplayMetrics().density); }
}
