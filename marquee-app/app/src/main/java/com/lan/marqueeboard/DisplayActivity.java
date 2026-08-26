package com.lan.marqueeboard;

import android.app.Activity;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.Typeface;
import android.os.Bundle;
import android.view.GestureDetector;
import android.view.MotionEvent;
import android.view.View;
import android.view.WindowManager;
import android.widget.Toast;

public class DisplayActivity extends Activity {

    private MarqueeView marqueeView;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        hideSystemUi();

        String text = getIntent().getStringExtra("text");
        if (text == null || text.trim().isEmpty()) text = "YEEEEEE~";
        int textColor = getIntent().getIntExtra("textColor", Color.WHITE);
        int textSizeSp = getIntent().getIntExtra("textSizeSp", 72);
        int speedDp = getIntent().getIntExtra("speedDp", 180);
        boolean rtl = getIntent().getBooleanExtra("rtl", true);

        marqueeView = new MarqueeView(text, textColor, textSizeSp, speedDp, rtl);
        setContentView(marqueeView);
        Toast.makeText(this, "轻点暂停 / 继续 · 长按返回设置", Toast.LENGTH_SHORT).show();
    }

    @Override
    public void onWindowFocusChanged(boolean hasFocus) {
        super.onWindowFocusChanged(hasFocus);
        if (hasFocus) hideSystemUi();
    }

    private void hideSystemUi() {
        getWindow().getDecorView().setSystemUiVisibility(
                View.SYSTEM_UI_FLAG_FULLSCREEN
                        | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                        | View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
                        | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
                        | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
                        | View.SYSTEM_UI_FLAG_LAYOUT_STABLE
        );
    }

    private class MarqueeView extends View {
        private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG | Paint.SUBPIXEL_TEXT_FLAG);
        private final GestureDetector detector;
        private final String text;
        private final boolean rtl;
        private final float speedPxPerSecond;
        private float textWidth = 1f;
        private float x = 0f;
        private long lastFrameNs = 0L;
        private boolean paused = false;
        private boolean positionInitialized = false;

        MarqueeView(String text, int textColor, int textSizeSp, int speedDp, boolean rtl) {
            super(DisplayActivity.this);
            this.text = text;
            this.rtl = rtl;
            float density = getResources().getDisplayMetrics().density;
            float scaledDensity = getResources().getDisplayMetrics().scaledDensity;
            this.speedPxPerSecond = speedDp * density;

            paint.setColor(textColor);
            paint.setTextSize(textSizeSp * scaledDensity);
            paint.setTypeface(Typeface.create(Typeface.DEFAULT, Typeface.BOLD));
            paint.setTextAlign(Paint.Align.LEFT);

            setBackgroundColor(Color.BLACK);
            setFocusable(true);
            detector = new GestureDetector(DisplayActivity.this, new GestureDetector.SimpleOnGestureListener() {
                @Override
                public boolean onDown(MotionEvent e) {
                    return true;
                }

                @Override
                public boolean onSingleTapConfirmed(MotionEvent e) {
                    paused = !paused;
                    if (!paused) {
                        lastFrameNs = 0L;
                        postInvalidateOnAnimation();
                    }
                    Toast.makeText(DisplayActivity.this, paused ? "已暂停" : "继续滚动", Toast.LENGTH_SHORT).show();
                    return true;
                }

                @Override
                public void onLongPress(MotionEvent e) {
                    finish();
                }
            });
        }

        @Override
        protected void onSizeChanged(int w, int h, int oldw, int oldh) {
            super.onSizeChanged(w, h, oldw, oldh);
            textWidth = Math.max(1f, paint.measureText(text));
            x = rtl ? w : -textWidth;
            positionInitialized = true;
            lastFrameNs = 0L;
        }

        @Override
        protected void onDraw(Canvas canvas) {
            super.onDraw(canvas);
            if (!positionInitialized) return;

            long now = System.nanoTime();
            if (!paused) {
                if (lastFrameNs != 0L) {
                    float deltaSeconds = Math.min(0.05f, (now - lastFrameNs) / 1_000_000_000f);
                    float delta = speedPxPerSecond * deltaSeconds;
                    x += rtl ? -delta : delta;
                }
                lastFrameNs = now;
            }

            Paint.FontMetrics fm = paint.getFontMetrics();
            float baseline = getHeight() / 2f - (fm.ascent + fm.descent) / 2f;
            canvas.drawText(text, x, baseline, paint);

            if (!paused) {
                if (rtl && x < -textWidth) x = getWidth();
                if (!rtl && x > getWidth()) x = -textWidth;
                postInvalidateOnAnimation();
            }
        }

        @Override
        public boolean onTouchEvent(MotionEvent event) {
            return detector.onTouchEvent(event) || super.onTouchEvent(event);
        }
    }
}
