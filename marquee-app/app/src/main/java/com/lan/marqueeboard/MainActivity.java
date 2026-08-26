package com.lan.marqueeboard;

import android.app.Activity;
import android.content.Intent;
import android.content.SharedPreferences;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.Bundle;
import android.provider.OpenableColumns;
import android.text.InputType;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.EditText;
import android.widget.HorizontalScrollView;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.RadioButton;
import android.widget.RadioGroup;
import android.widget.ScrollView;
import android.widget.SeekBar;
import android.widget.Switch;
import android.widget.TextView;
import android.widget.Toast;

public class MainActivity extends Activity {

    private static final int PICK_BACKGROUND = 2201;

    private EditText textInput;
    private SeekBar sizeSeek;
    private SeekBar speedSeek;
    private SeekBar dimSeek;
    private TextView sizeValue;
    private TextView speedValue;
    private TextView dimValue;
    private RadioGroup directionGroup;
    private RadioGroup fitGroup;
    private Switch rainbowSwitch;
    private Switch neonSwitch;
    private Switch flashSwitch;
    private Switch mirrorSwitch;
    private Switch ledSwitch;
    private Switch ultraSwitch;
    private ImageView backgroundPreview;
    private TextView backgroundState;

    private int selectedColor = Color.WHITE;
    private String backgroundUri = "";
    private SharedPreferences prefs;

    private final int[] colors = new int[]{
            Color.WHITE,
            Color.rgb(113, 244, 255),
            Color.rgb(255, 101, 189),
            Color.rgb(255, 220, 88),
            Color.rgb(133, 255, 124),
            Color.rgb(183, 132, 255),
            Color.rgb(255, 105, 82)
    };

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        prefs = getSharedPreferences("marquee_board", MODE_PRIVATE);
        backgroundUri = prefs.getString("background_uri", "");
        buildUi();
        restoreBackgroundPreview();
    }

    private void buildUi() {
        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        scroll.setBackgroundColor(Color.rgb(8, 10, 13));

        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(dp(18), dp(24), dp(18), dp(30));
        scroll.addView(root, new ScrollView.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT));

        TextView badge = new TextView(this);
        badge.setText("V0.2 · CUSTOM BACKGROUND");
        badge.setTextColor(Color.rgb(5, 16, 20));
        badge.setTextSize(11);
        badge.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        badge.setGravity(Gravity.CENTER);
        badge.setPadding(dp(12), dp(6), dp(12), dp(6));
        badge.setBackground(rounded(Color.rgb(113, 244, 255), 999, Color.TRANSPARENT, 0));
        LinearLayout.LayoutParams badgeLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT,
                ViewGroup.LayoutParams.WRAP_CONTENT);
        badgeLp.bottomMargin = dp(12);
        root.addView(badge, badgeLp);

        TextView title = new TextView(this);
        title.setText("滚起来");
        title.setTextColor(Color.WHITE);
        title.setTextSize(30);
        title.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        root.addView(title, matchWrap());

        TextView subtitle = new TextView(this);
        subtitle.setText("把手机变成一块自定义的全屏滚动展示牌");
        subtitle.setTextColor(Color.rgb(145, 151, 161));
        subtitle.setTextSize(14);
        LinearLayout.LayoutParams subLp = matchWrap();
        subLp.topMargin = dp(5);
        subLp.bottomMargin = dp(22);
        root.addView(subtitle, subLp);

        addLabel(root, "展示文案");
        textInput = new EditText(this);
        textInput.setText("YEEEEEE~\n岚岚最酷\n请给我让个道谢谢");
        textInput.setHint("每一行是一句，会按顺序循环");
        textInput.setTextColor(Color.WHITE);
        textInput.setHintTextColor(Color.rgb(100, 106, 116));
        textInput.setTextSize(17);
        textInput.setMinLines(4);
        textInput.setGravity(Gravity.TOP | Gravity.START);
        textInput.setInputType(InputType.TYPE_CLASS_TEXT
                | InputType.TYPE_TEXT_FLAG_MULTI_LINE
                | InputType.TYPE_TEXT_FLAG_CAP_SENTENCES);
        textInput.setPadding(dp(15), dp(14), dp(15), dp(14));
        textInput.setBackground(card());
        LinearLayout.LayoutParams inputLp = matchWrap();
        inputLp.bottomMargin = dp(20);
        root.addView(textInput, inputLp);

        addLabel(root, "字体大小");
        LinearLayout sizeHeader = valueHeader("40 — 220 sp");
        sizeValue = (TextView) sizeHeader.getChildAt(1);
        root.addView(sizeHeader, matchWrap());
        sizeSeek = new SeekBar(this);
        sizeSeek.setMax(180);
        sizeSeek.setProgress(68);
        sizeValue.setText("108 sp");
        sizeSeek.setOnSeekBarChangeListener(new SimpleSeekListener() {
            @Override public void onProgressChanged(SeekBar seekBar, int progress, boolean fromUser) {
                sizeValue.setText((40 + progress) + " sp");
            }
        });
        LinearLayout.LayoutParams sizeLp = matchWrap();
        sizeLp.bottomMargin = dp(14);
        root.addView(sizeSeek, sizeLp);

        addLabel(root, "滚动速度");
        LinearLayout speedHeader = valueHeader("慢 — 快");
        speedValue = (TextView) speedHeader.getChildAt(1);
        root.addView(speedHeader, matchWrap());
        speedSeek = new SeekBar(this);
        speedSeek.setMax(58);
        speedSeek.setProgress(12);
        speedValue.setText("14");
        speedSeek.setOnSeekBarChangeListener(new SimpleSeekListener() {
            @Override public void onProgressChanged(SeekBar seekBar, int progress, boolean fromUser) {
                speedValue.setText(String.valueOf(2 + progress));
            }
        });
        LinearLayout.LayoutParams speedLp = matchWrap();
        speedLp.bottomMargin = dp(16);
        root.addView(speedSeek, speedLp);

        addLabel(root, "滚动方向");
        directionGroup = new RadioGroup(this);
        directionGroup.setOrientation(RadioGroup.HORIZONTAL);
        directionGroup.setPadding(dp(10), dp(6), dp(10), dp(6));
        directionGroup.setBackground(card());
        RadioButton rtl = radio("右 → 左", 1001, true);
        RadioButton ltr = radio("左 → 右", 1002, false);
        directionGroup.addView(rtl, new RadioGroup.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        directionGroup.addView(ltr, new RadioGroup.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        directionGroup.check(1001);
        LinearLayout.LayoutParams directionLp = matchWrap();
        directionLp.bottomMargin = dp(18);
        root.addView(directionGroup, directionLp);

        addLabel(root, "文字颜色");
        root.addView(makeColorPicker());

        addLabel(root, "自定义背景图");
        LinearLayout bgCard = new LinearLayout(this);
        bgCard.setOrientation(LinearLayout.VERTICAL);
        bgCard.setPadding(dp(12), dp(12), dp(12), dp(12));
        bgCard.setBackground(card());

        backgroundPreview = new ImageView(this);
        backgroundPreview.setScaleType(ImageView.ScaleType.CENTER_CROP);
        backgroundPreview.setBackgroundColor(Color.rgb(13, 15, 19));
        LinearLayout.LayoutParams previewLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(150));
        bgCard.addView(backgroundPreview, previewLp);

        backgroundState = new TextView(this);
        backgroundState.setText("未选择背景 · 默认纯黑");
        backgroundState.setTextColor(Color.rgb(158, 164, 173));
        backgroundState.setTextSize(13);
        LinearLayout.LayoutParams stateLp = matchWrap();
        stateLp.topMargin = dp(9);
        bgCard.addView(backgroundState, stateLp);

        LinearLayout bgButtons = new LinearLayout(this);
        bgButtons.setOrientation(LinearLayout.HORIZONTAL);
        bgButtons.setPadding(0, dp(10), 0, 0);
        Button chooseBg = smallButton("从相册选择");
        chooseBg.setOnClickListener(v -> pickBackground());
        Button clearBg = smallButton("恢复纯黑");
        clearBg.setOnClickListener(v -> clearBackground());
        LinearLayout.LayoutParams half = new LinearLayout.LayoutParams(0, dp(48), 1f);
        half.rightMargin = dp(8);
        bgButtons.addView(chooseBg, half);
        bgButtons.addView(clearBg, new LinearLayout.LayoutParams(0, dp(48), 1f));
        bgCard.addView(bgButtons);

        TextView dimLabel = new TextView(this);
        dimLabel.setText("背景压暗");
        dimLabel.setTextColor(Color.WHITE);
        dimLabel.setTextSize(14);
        dimLabel.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        LinearLayout.LayoutParams dl = matchWrap();
        dl.topMargin = dp(14);
        bgCard.addView(dimLabel, dl);

        LinearLayout dimHeader = valueHeader("0% — 85%");
        dimValue = (TextView) dimHeader.getChildAt(1);
        bgCard.addView(dimHeader, matchWrap());
        dimSeek = new SeekBar(this);
        dimSeek.setMax(85);
        int savedDim = prefs.getInt("background_dim", 35);
        dimSeek.setProgress(savedDim);
        dimValue.setText(savedDim + "%");
        dimSeek.setOnSeekBarChangeListener(new SimpleSeekListener() {
            @Override public void onProgressChanged(SeekBar seekBar, int progress, boolean fromUser) {
                dimValue.setText(progress + "%");
                prefs.edit().putInt("background_dim", progress).apply();
            }
        });
        bgCard.addView(dimSeek, matchWrap());

        TextView fitLabel = new TextView(this);
        fitLabel.setText("图片显示方式");
        fitLabel.setTextColor(Color.WHITE);
        fitLabel.setTextSize(14);
        fitLabel.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        LinearLayout.LayoutParams fl = matchWrap();
        fl.topMargin = dp(10);
        bgCard.addView(fitLabel, fl);

        fitGroup = new RadioGroup(this);
        fitGroup.setOrientation(RadioGroup.HORIZONTAL);
        RadioButton crop = radio("裁切铺满", 2001, true);
        RadioButton fit = radio("完整显示", 2002, false);
        fitGroup.addView(crop, new RadioGroup.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        fitGroup.addView(fit, new RadioGroup.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        int savedFit = prefs.getInt("background_fit", 2001);
        fitGroup.check(savedFit);
        fitGroup.setOnCheckedChangeListener((group, checkedId) -> {
            prefs.edit().putInt("background_fit", checkedId).apply();
            backgroundPreview.setScaleType(checkedId == 2001
                    ? ImageView.ScaleType.CENTER_CROP
                    : ImageView.ScaleType.FIT_CENTER);
        });
        bgCard.addView(fitGroup, matchWrap());

        LinearLayout.LayoutParams bgCardLp = matchWrap();
        bgCardLp.bottomMargin = dp(18);
        root.addView(bgCard, bgCardLp);

        addLabel(root, "文字特效");
        LinearLayout fx = new LinearLayout(this);
        fx.setOrientation(LinearLayout.VERTICAL);
        fx.setPadding(dp(12), dp(8), dp(12), dp(8));
        fx.setBackground(card());
        rainbowSwitch = featureSwitch("彩虹流光", "文字颜色会动态流动");
        neonSwitch = featureSwitch("霓虹辉光", "给文字增加发光边缘");
        flashSwitch = featureSwitch("闪烁呼吸", "文字明暗周期变化");
        mirrorSwitch = featureSwitch("镜像文字", "适合隔玻璃反向展示");
        ledSwitch = featureSwitch("LED 点阵", "模拟电子灯牌的点阵效果");
        ultraSwitch = featureSwitch("鬼畜极速", "把当前速度再翻一倍");
        fx.addView(rainbowSwitch);
        fx.addView(neonSwitch);
        fx.addView(flashSwitch);
        fx.addView(mirrorSwitch);
        fx.addView(ledSwitch);
        fx.addView(ultraSwitch);
        LinearLayout.LayoutParams fxLp = matchWrap();
        fxLp.bottomMargin = dp(20);
        root.addView(fx, fxLp);

        Button start = new Button(this);
        start.setText("开始展示");
        start.setAllCaps(false);
        start.setTextSize(18);
        start.setTextColor(Color.rgb(5, 18, 22));
        start.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        start.setBackground(rounded(Color.rgb(113, 244, 255), 18, Color.TRANSPARENT, 0));
        start.setOnClickListener(v -> launchDisplay());
        root.addView(start, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(58)));

        TextView hint = new TextView(this);
        hint.setText("展示时：轻点暂停 / 继续 · 长按返回 · 屏幕保持常亮");
        hint.setTextColor(Color.rgb(112, 119, 129));
        hint.setTextSize(12);
        hint.setGravity(Gravity.CENTER);
        LinearLayout.LayoutParams hintLp = matchWrap();
        hintLp.topMargin = dp(12);
        root.addView(hint, hintLp);

        setContentView(scroll);
    }

    private void pickBackground() {
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        intent.setType("image/*");
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION
                | Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION);
        startActivityForResult(intent, PICK_BACKGROUND);
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != PICK_BACKGROUND || resultCode != RESULT_OK || data == null) return;
        Uri uri = data.getData();
        if (uri == null) return;

        try {
            int flags = data.getFlags() & (Intent.FLAG_GRANT_READ_URI_PERMISSION
                    | Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
            getContentResolver().takePersistableUriPermission(uri, flags);
        } catch (Exception ignored) {
        }

        backgroundUri = uri.toString();
        prefs.edit().putString("background_uri", backgroundUri).apply();
        restoreBackgroundPreview();
    }

    private void restoreBackgroundPreview() {
        if (backgroundPreview == null) return;
        if (backgroundUri == null || backgroundUri.isEmpty()) {
            backgroundPreview.setImageDrawable(null);
            backgroundPreview.setBackgroundColor(Color.rgb(13, 15, 19));
            backgroundState.setText("未选择背景 · 默认纯黑");
            return;
        }
        try {
            Uri uri = Uri.parse(backgroundUri);
            backgroundPreview.setImageURI(uri);
            int fit = prefs.getInt("background_fit", 2001);
            backgroundPreview.setScaleType(fit == 2001
                    ? ImageView.ScaleType.CENTER_CROP
                    : ImageView.ScaleType.FIT_CENTER);
            backgroundState.setText("已选择自定义背景");
        } catch (Exception e) {
            clearBackground();
        }
    }

    private void clearBackground() {
        backgroundUri = "";
        prefs.edit().remove("background_uri").apply();
        if (backgroundPreview != null) {
            backgroundPreview.setImageDrawable(null);
            backgroundPreview.setBackgroundColor(Color.rgb(13, 15, 19));
        }
        if (backgroundState != null) backgroundState.setText("未选择背景 · 默认纯黑");
    }

    private void launchDisplay() {
        String raw = textInput.getText().toString().trim();
        if (raw.isEmpty()) {
            Toast.makeText(this, "先写点东西再让它滚起来 😎", Toast.LENGTH_SHORT).show();
            return;
        }

        Intent intent = new Intent(this, DisplayActivity.class);
        intent.putExtra("phrases", raw);
        intent.putExtra("textColor", selectedColor);
        intent.putExtra("textSizeSp", 40 + sizeSeek.getProgress());
        intent.putExtra("speed", 2 + speedSeek.getProgress());
        intent.putExtra("rtl", directionGroup.getCheckedRadioButtonId() == 1001);
        intent.putExtra("rainbow", rainbowSwitch.isChecked());
        intent.putExtra("neon", neonSwitch.isChecked());
        intent.putExtra("flash", flashSwitch.isChecked());
        intent.putExtra("mirror", mirrorSwitch.isChecked());
        intent.putExtra("led", ledSwitch.isChecked());
        intent.putExtra("ultra", ultraSwitch.isChecked());
        intent.putExtra("backgroundUri", backgroundUri == null ? "" : backgroundUri);
        intent.putExtra("backgroundDim", dimSeek.getProgress());
        intent.putExtra("backgroundFit", fitGroup.getCheckedRadioButtonId());
        startActivity(intent);
    }

    private View makeColorPicker() {
        HorizontalScrollView hsv = new HorizontalScrollView(this);
        hsv.setHorizontalScrollBarEnabled(false);
        LinearLayout row = new LinearLayout(this);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setPadding(dp(10), dp(10), dp(4), dp(10));
        hsv.addView(row, new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT,
                ViewGroup.LayoutParams.WRAP_CONTENT));

        for (int color : colors) {
            View swatch = new View(this);
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(dp(38), dp(38));
            lp.rightMargin = dp(10);
            swatch.setLayoutParams(lp);
            swatch.setBackground(colorCircle(color, color == selectedColor));
            swatch.setOnClickListener(v -> {
                selectedColor = color;
                for (int i = 0; i < row.getChildCount(); i++) {
                    row.getChildAt(i).setBackground(colorCircle(colors[i], colors[i] == selectedColor));
                }
            });
            row.addView(swatch);
        }
        LinearLayout.LayoutParams lp = matchWrap();
        lp.bottomMargin = dp(18);
        hsv.setLayoutParams(lp);
        hsv.setBackground(card());
        return hsv;
    }

    private Switch featureSwitch(String title, String subtitle) {
        Switch sw = new Switch(this);
        sw.setText(title + "\n" + subtitle);
        sw.setTextColor(Color.WHITE);
        sw.setTextSize(14);
        sw.setPadding(0, dp(7), 0, dp(7));
        return sw;
    }

    private Button smallButton(String text) {
        Button b = new Button(this);
        b.setText(text);
        b.setAllCaps(false);
        b.setTextColor(Color.WHITE);
        b.setTextSize(14);
        b.setBackground(rounded(Color.rgb(29, 34, 41), 13, Color.rgb(56, 63, 73), 1));
        return b;
    }

    private RadioButton radio(String text, int id, boolean checked) {
        RadioButton rb = new RadioButton(this);
        rb.setId(id);
        rb.setText(text);
        rb.setTextColor(Color.WHITE);
        rb.setTextSize(14);
        rb.setChecked(checked);
        return rb;
    }

    private LinearLayout valueHeader(String leftText) {
        LinearLayout row = new LinearLayout(this);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(Gravity.CENTER_VERTICAL);
        TextView left = new TextView(this);
        left.setText(leftText);
        left.setTextColor(Color.rgb(112, 119, 129));
        left.setTextSize(12);
        row.addView(left, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        TextView right = new TextView(this);
        right.setTextColor(Color.rgb(113, 244, 255));
        right.setTextSize(13);
        right.setTypeface(Typeface.MONOSPACE, Typeface.BOLD);
        row.addView(right);
        return row;
    }

    private void addLabel(LinearLayout root, String text) {
        TextView label = new TextView(this);
        label.setText(text);
        label.setTextColor(Color.rgb(205, 211, 220));
        label.setTextSize(13);
        label.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        LinearLayout.LayoutParams lp = matchWrap();
        lp.topMargin = dp(4);
        lp.bottomMargin = dp(7);
        root.addView(label, lp);
    }

    private GradientDrawable card() {
        return rounded(Color.rgb(16, 19, 24), 17, Color.rgb(38, 44, 53), 1);
    }

    private GradientDrawable colorCircle(int color, boolean selected) {
        GradientDrawable d = new GradientDrawable();
        d.setShape(GradientDrawable.OVAL);
        d.setColor(color);
        d.setStroke(dp(selected ? 3 : 1), selected ? Color.WHITE : Color.rgb(53, 59, 68));
        return d;
    }

    private GradientDrawable rounded(int fill, int radiusDp, int stroke, int strokeWidthDp) {
        GradientDrawable d = new GradientDrawable();
        d.setColor(fill);
        d.setCornerRadius(dp(radiusDp));
        if (strokeWidthDp > 0) d.setStroke(dp(strokeWidthDp), stroke);
        return d;
    }

    private LinearLayout.LayoutParams matchWrap() {
        return new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT);
    }

    private int dp(int value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }

    private abstract static class SimpleSeekListener implements SeekBar.OnSeekBarChangeListener {
        @Override public void onStartTrackingTouch(SeekBar seekBar) {}
        @Override public void onStopTrackingTouch(SeekBar seekBar) {}
    }
}
