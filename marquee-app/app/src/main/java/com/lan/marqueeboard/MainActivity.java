package com.lan.marqueeboard;

import android.app.Activity;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.text.InputType;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.EditText;
import android.widget.HorizontalScrollView;
import android.widget.LinearLayout;
import android.widget.RadioButton;
import android.widget.RadioGroup;
import android.widget.SeekBar;
import android.widget.TextView;
import android.widget.Toast;

public class MainActivity extends Activity {

    private EditText textInput;
    private TextView sizeValue;
    private TextView speedValue;
    private TextView colorPreview;
    private int selectedColor = Color.WHITE;
    private int textSizeSp = 72;
    private int speedDp = 180;
    private boolean rightToLeft = true;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        buildUi();
    }

    private void buildUi() {
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(dp(20), dp(22), dp(20), dp(26));
        root.setBackgroundColor(Color.rgb(8, 9, 11));

        TextView title = new TextView(this);
        title.setText("滚起来  ·  V0.1");
        title.setTextColor(Color.WHITE);
        title.setTextSize(27);
        title.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        root.addView(title, matchWrap());

        TextView subtitle = new TextView(this);
        subtitle.setText("把手机变成一块会跑字的全屏展示牌");
        subtitle.setTextColor(Color.rgb(145, 150, 160));
        subtitle.setTextSize(14);
        LinearLayout.LayoutParams subLp = matchWrap();
        subLp.topMargin = dp(6);
        subLp.bottomMargin = dp(24);
        root.addView(subtitle, subLp);

        addLabel(root, "展示文字");
        textInput = new EditText(this);
        textInput.setText("YEEEEEE~");
        textInput.setTextColor(Color.WHITE);
        textInput.setHintTextColor(Color.rgb(100, 105, 115));
        textInput.setHint("输入要展示的内容");
        textInput.setTextSize(20);
        textInput.setSingleLine(true);
        textInput.setInputType(InputType.TYPE_CLASS_TEXT);
        textInput.setPadding(dp(16), 0, dp(16), 0);
        textInput.setBackground(rounded(Color.rgb(24, 27, 33), dp(14), Color.rgb(58, 63, 73), 1));
        LinearLayout.LayoutParams inputLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(58));
        inputLp.bottomMargin = dp(20);
        root.addView(textInput, inputLp);

        addLabel(root, "字号");
        LinearLayout sizeRow = valueRow();
        sizeValue = valueText(textSizeSp + " sp");
        sizeRow.addView(sizeValue);
        root.addView(sizeRow, matchWrap());

        SeekBar sizeSeek = new SeekBar(this);
        sizeSeek.setMax(88);
        sizeSeek.setProgress(textSizeSp - 32);
        sizeSeek.setOnSeekBarChangeListener(new SimpleSeekListener() {
            @Override public void onProgressChanged(SeekBar seekBar, int progress, boolean fromUser) {
                textSizeSp = 32 + progress;
                sizeValue.setText(textSizeSp + " sp");
            }
        });
        LinearLayout.LayoutParams seekLp = matchWrap();
        seekLp.bottomMargin = dp(16);
        root.addView(sizeSeek, seekLp);

        addLabel(root, "滚动速度");
        LinearLayout speedRow = valueRow();
        speedValue = valueText(speedDp + " dp/s");
        speedRow.addView(speedValue);
        root.addView(speedRow, matchWrap());

        SeekBar speedSeek = new SeekBar(this);
        speedSeek.setMax(55);
        speedSeek.setProgress((speedDp - 60) / 10);
        speedSeek.setOnSeekBarChangeListener(new SimpleSeekListener() {
            @Override public void onProgressChanged(SeekBar seekBar, int progress, boolean fromUser) {
                speedDp = 60 + progress * 10;
                speedValue.setText(speedDp + " dp/s");
            }
        });
        LinearLayout.LayoutParams speedSeekLp = matchWrap();
        speedSeekLp.bottomMargin = dp(18);
        root.addView(speedSeek, speedSeekLp);

        addLabel(root, "方向");
        RadioGroup direction = new RadioGroup(this);
        direction.setOrientation(RadioGroup.HORIZONTAL);
        RadioButton rtl = radio("← 右进左出");
        RadioButton ltr = radio("左进右出 →");
        rtl.setChecked(true);
        direction.addView(rtl);
        direction.addView(ltr);
        direction.setOnCheckedChangeListener((group, checkedId) -> rightToLeft = checkedId == rtl.getId());
        LinearLayout.LayoutParams dirLp = matchWrap();
        dirLp.bottomMargin = dp(20);
        root.addView(direction, dirLp);

        addLabel(root, "文字颜色");
        HorizontalScrollView scroller = new HorizontalScrollView(this);
        scroller.setHorizontalScrollBarEnabled(false);
        LinearLayout palette = new LinearLayout(this);
        palette.setOrientation(LinearLayout.HORIZONTAL);
        scroller.addView(palette);
        int[] colors = {
                Color.WHITE,
                Color.rgb(255, 78, 90),
                Color.rgb(255, 215, 67),
                Color.rgb(88, 225, 255),
                Color.rgb(255, 95, 210),
                Color.rgb(115, 255, 133)
        };
        for (int color : colors) {
            Button swatch = new Button(this);
            swatch.setMinWidth(0);
            swatch.setMinimumWidth(0);
            swatch.setPadding(0, 0, 0, 0);
            swatch.setBackground(rounded(color, dp(24), color, 0));
            swatch.setOnClickListener(v -> {
                selectedColor = color;
                colorPreview.setTextColor(color);
                colorPreview.setText("● 已选择");
            });
            LinearLayout.LayoutParams swatchLp = new LinearLayout.LayoutParams(dp(46), dp(46));
            swatchLp.rightMargin = dp(10);
            palette.addView(swatch, swatchLp);
        }
        LinearLayout.LayoutParams scrollLp = matchWrap();
        scrollLp.bottomMargin = dp(8);
        root.addView(scroller, scrollLp);

        colorPreview = new TextView(this);
        colorPreview.setText("● 已选择");
        colorPreview.setTextColor(selectedColor);
        colorPreview.setTextSize(14);
        LinearLayout.LayoutParams previewLp = matchWrap();
        previewLp.bottomMargin = dp(24);
        root.addView(colorPreview, previewLp);

        Button start = new Button(this);
        start.setText("开始展示");
        start.setAllCaps(false);
        start.setTextSize(18);
        start.setTextColor(Color.rgb(5, 18, 22));
        start.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        start.setBackground(rounded(Color.rgb(184, 244, 255), dp(18), Color.TRANSPARENT, 0));
        start.setOnClickListener(v -> launchDisplay());
        LinearLayout.LayoutParams startLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(60));
        root.addView(start, startLp);

        TextView hint = new TextView(this);
        hint.setText("展示时：轻点暂停 / 继续 · 长按返回设置 · 屏幕会保持常亮");
        hint.setTextColor(Color.rgb(115, 120, 130));
        hint.setTextSize(12);
        hint.setGravity(Gravity.CENTER);
        LinearLayout.LayoutParams hintLp = matchWrap();
        hintLp.topMargin = dp(14);
        root.addView(hint, hintLp);

        android.widget.ScrollView page = new android.widget.ScrollView(this);
        page.setFillViewport(true);
        page.addView(root);
        setContentView(page);
    }

    private void launchDisplay() {
        String text = textInput.getText().toString().trim();
        if (text.isEmpty()) {
            Toast.makeText(this, "先写点东西再让它滚起来 😎", Toast.LENGTH_SHORT).show();
            return;
        }
        Intent intent = new Intent(this, DisplayActivity.class);
        intent.putExtra("text", text);
        intent.putExtra("textColor", selectedColor);
        intent.putExtra("textSizeSp", textSizeSp);
        intent.putExtra("speedDp", speedDp);
        intent.putExtra("rtl", rightToLeft);
        startActivity(intent);
    }

    private void addLabel(LinearLayout root, String text) {
        TextView v = new TextView(this);
        v.setText(text);
        v.setTextColor(Color.rgb(205, 210, 218));
        v.setTextSize(13);
        v.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        LinearLayout.LayoutParams lp = matchWrap();
        lp.bottomMargin = dp(7);
        root.addView(v, lp);
    }

    private LinearLayout valueRow() {
        LinearLayout row = new LinearLayout(this);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(Gravity.END);
        return row;
    }

    private TextView valueText(String text) {
        TextView v = new TextView(this);
        v.setText(text);
        v.setTextColor(Color.rgb(145, 150, 160));
        v.setTextSize(13);
        return v;
    }

    private RadioButton radio(String text) {
        RadioButton r = new RadioButton(this);
        r.setId(View.generateViewId());
        r.setText(text);
        r.setTextColor(Color.WHITE);
        r.setTextSize(14);
        r.setPadding(0, 0, dp(18), 0);
        return r;
    }

    private LinearLayout.LayoutParams matchWrap() {
        return new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
    }

    private GradientDrawable rounded(int fill, int radius, int stroke, int strokeWidth) {
        GradientDrawable d = new GradientDrawable();
        d.setColor(fill);
        d.setCornerRadius(radius);
        if (strokeWidth > 0) d.setStroke(dp(strokeWidth), stroke);
        return d;
    }

    private int dp(int value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }

    private abstract static class SimpleSeekListener implements SeekBar.OnSeekBarChangeListener {
        @Override public void onStartTrackingTouch(SeekBar seekBar) {}
        @Override public void onStopTrackingTouch(SeekBar seekBar) {}
    }
}
