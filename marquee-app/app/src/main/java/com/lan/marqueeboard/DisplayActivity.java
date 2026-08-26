package com.lan.marqueeboard;

import android.app.Activity;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.LinearGradient;
import android.graphics.Matrix;
import android.graphics.Paint;
import android.graphics.Shader;
import android.graphics.Typeface;
import android.net.Uri;
import android.os.Bundle;
import android.text.TextUtils;
import android.view.GestureDetector;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.view.WindowManager;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.TextView;
import android.widget.Toast;

import java.util.ArrayList;
import java.util.List;

public class DisplayActivity extends Activity {

    private MarqueeView marqueeView;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        hideSystemUi();

        FrameLayout root = new FrameLayout(this);
        root.setBackgroundColor(Color.BLACK);

        String backgroundUri = getIntent().getStringExtra("backgroundUri");
        int backgroundDim = getIntent().getIntExtra("backgroundDim", 35);
        int backgroundFit = getIntent().getIntExtra("backgroundFit", 2001);

        if (backgroundUri != null && !backgroundUri.isEmpty()) {
            try {
                ImageView image = new ImageView(this);
                image.setImageURI(Uri.parse(backgroundUri));
                image.setScaleType(backgroundFit == 2002
                        ? ImageView.ScaleType.FIT_CENTER
                        : ImageView.ScaleType.CENTER_CROP);
                image.setBackgroundColor(Color.BLACK);
                root.addView(image, new FrameLayout.LayoutParams(
                        FrameLayout.LayoutParams.MATCH_PARENT,
                        FrameLayout.LayoutParams.MATCH_PARENT));
            } catch (Exception e) {
                Toast.makeText(this, "背景图读取失败，已使用纯黑背景", Toast.LENGTH_SHORT).show();
            }
        }

        View dimOverlay = new View(this);
        dimOverlay.setBackgroundColor(Color.BLACK);
        dimOverlay.setAlpha(Math.max(0f, Math.min(0.85f, backgroundDim / 100f)));
        root.addView(dimOverlay, new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT,
                FrameLayout.LayoutParams.MATCH_PARENT));

        String phrases = getIntent().getStringExtra("phrases");
        int textColor = getIntent().getIntExtra("textColor", Color.WHITE);
        int textSizeSp = getIntent().getIntExtra("textSizeSp", 108);
        int speed = getIntent().getIntExtra("speed", 14);
        boolean rtl = getIntent().getBooleanExtra("rtl", true);
        boolean rainbow = getIntent().getBooleanExtra("rainbow", false);
        boolean neon = getIntent().getBooleanExtra("neon", false);
        boolean flash = getIntent().getBooleanExtra("flash", false);
        boolean mirror = getIntent().getBooleanExtra("mirror", false);
        boolean led = getIntent().getBooleanExtra("led", false);
        boolean ultra = getIntent().getBooleanExtra("ultra", false);

        marqueeView = new MarqueeView(
                phrases, textColor, textSizeSp, speed, rtl,
                rainbow, neon, flash, mirror, led, ultra);
        root.addView(marqueeView, new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT,
                FrameLayout.LayoutParams.MATCH_PARENT));

        TextView hint = new TextView(this);
        hint.setText("轻点暂停 / 继续  ·  长按返回");
        hint.setTextColor(Color.argb(155, 255, 255, 255));
        hint.setTextSize(12);
        hint.setGravity(Gravity.CENTER);
        FrameLayout.LayoutParams hintLp = new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.WRAP_CONTENT,
                FrameLayout.LayoutParams.WRAP_CONTENT);
        hintLp.gravity = Gravity.TOP | Gravity.CENTER_HORIZONTAL;
        hintLp.topMargin = dp(8);
        root.addView(hint, hintLp);

        setContentView(root);
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

    private int dp(int value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }

    private class MarqueeView extends View {
        private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG | Paint.SUBPIXEL_TEXT_FLAG);
        private final Paint ledPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Paint ledSourcePaint = new Paint(Paint.ANTI_ALIAS_FLAG | Paint.SUBPIXEL_TEXT_FLAG);
        private final GestureDetector detector;
        private final List<String> phrases = new ArrayList<>();

        private final boolean rtl;
        private final boolean rainbow;
        private final boolean neon;
        private final boolean flash;
        private final boolean mirror;
        private final boolean led;
        private final boolean ultra;
        private final int baseColor;
        private final float speedPxPerSecond;
        private final float textSizePx;

        private float textWidth = 1f;
        private float x = 0f;
        private long lastFrameNs = 0L;
        private boolean paused = false;
        private boolean positionInitialized = false;
        private int phraseIndex = 0;

        private LinearGradient rainbowShader;
        private final Matrix shaderMatrix = new Matrix();
        private Bitmap ledBitmap;
        private Canvas ledCanvas;

        MarqueeView(String rawPhrases,
                    int textColor,
                    int textSizeSp,
                    int speed,
                    boolean rtl,
                    boolean rainbow,
                    boolean neon,
                    boolean flash,
                    boolean mirror,
                    boolean led,
                    boolean ultra) {
            super(DisplayActivity.this);
            this.rtl = rtl;
            this.rainbow = rainbow;
            this.neon = neon;
            this.flash = flash;
            this.mirror = mirror;
            this.led = led;
            this.ultra = ultra;
            this.baseColor = textColor;

            if (rawPhrases != null) {
                String[] lines = rawPhrases.split("\\n");
                for (String line : lines) {
                    String clean = line.trim();
                    if (!clean.isEmpty()) phrases.add(clean);
                }
            }
            if (phrases.isEmpty()) phrases.add("YEEEEEE~");

            float density = getResources().getDisplayMetrics().density;
            float scaledDensity = getResources().getDisplayMetrics().scaledDensity;
            this.textSizePx = textSizeSp * scaledDensity;
            this.speedPxPerSecond = Math.max(2, speed) * density * 26f;

            paint.setTextSize(textSizePx);
            paint.setTypeface(Typeface.create(Typeface.DEFAULT, Typeface.BOLD));
            paint.setTextAlign(Paint.Align.LEFT);
            paint.setColor(baseColor);

            ledSourcePaint.setTextSize(textSizePx);
            ledSourcePaint.setTypeface(Typeface.create(Typeface.DEFAULT, Typeface.BOLD));
            ledSourcePaint.setTextAlign(Paint.Align.LEFT);
            ledSourcePaint.setColor(Color.WHITE);

            if (neon || led) setLayerType(View.LAYER_TYPE_SOFTWARE, null);
            setFocusable(true);
            setClickable(true);

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
                    Toast.makeText(DisplayActivity.this,
                            paused ? "已暂停" : "继续滚动",
                            Toast.LENGTH_SHORT).show();
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
            rainbowShader = new LinearGradient(
                    0, 0, Math.max(1, w), 0,
                    new int[]{
                            Color.rgb(255, 76, 103),
                            Color.rgb(255, 221, 84),
                            Color.rgb(104, 255, 142),
                            Color.rgb(93, 235, 255),
                            Color.rgb(123, 139, 255),
                            Color.rgb(255, 102, 215),
                            Color.rgb(255, 76, 103)
                    },
                    null,
                    Shader.TileMode.CLAMP);
            preparePhrase(true);
            positionInitialized = true;
            lastFrameNs = 0L;
        }

        private String currentPhrase() {
            if (phrases.isEmpty()) return "YEEEEEE~";
            return phrases.get(phraseIndex % phrases.size());
        }

        private void preparePhrase(boolean resetPosition) {
            String text = currentPhrase();
            textWidth = Math.max(1f, paint.measureText(text));
            if (resetPosition) {
                x = rtl ? getWidth() + dp(8) : -textWidth - dp(8);
            }
            prepareLedBitmap();
        }

        private void prepareLedBitmap() {
            if (!led || getWidth() <= 0 || getHeight() <= 0) return;
            int bw = Math.max(1, (int) Math.ceil(textWidth + dp(30)));
            int bh = Math.max(1, (int) Math.ceil(textSizePx * 1.45f));
            try {
                ledBitmap = Bitmap.createBitmap(bw, bh, Bitmap.Config.ARGB_8888);
                ledCanvas = new Canvas(ledBitmap);
                Paint.FontMetrics fm = ledSourcePaint.getFontMetrics();
                float baseline = bh / 2f - (fm.ascent + fm.descent) / 2f;
                ledCanvas.drawText(currentPhrase(), dp(10), baseline, ledSourcePaint);
            } catch (OutOfMemoryError e) {
                ledBitmap = null;
                ledCanvas = null;
            }
        }

        private void advancePhrase() {
            phraseIndex = (phraseIndex + 1) % phrases.size();
            preparePhrase(true);
        }

        @Override
        protected void onDraw(Canvas canvas) {
            super.onDraw(canvas);
            if (!positionInitialized) return;

            long now = System.nanoTime();
            if (!paused) {
                if (lastFrameNs != 0L) {
                    float deltaSeconds = Math.min(0.05f,
                            (now - lastFrameNs) / 1_000_000_000f);
                    float speed = speedPxPerSecond * (ultra ? 2.15f : 1f);
                    float delta = speed * deltaSeconds;
                    x += rtl ? -delta : delta;
                }
                lastFrameNs = now;
            }

            if (rtl && x + textWidth < -dp(24)) advancePhrase();
            if (!rtl && x > getWidth() + dp(24)) advancePhrase();

            if (mirror) {
                canvas.save();
                canvas.scale(-1f, 1f, getWidth() / 2f, getHeight() / 2f);
            }

            if (led && ledBitmap != null) drawLed(canvas, now);
            else drawText(canvas, now);

            if (mirror) canvas.restore();

            if (!paused) postInvalidateOnAnimation();
        }

        private void drawText(Canvas canvas, long nowNs) {
            long nowMs = nowNs / 1_000_000L;
            paint.setShader(null);
            paint.clearShadowLayer();
            paint.setColor(baseColor);
            paint.setAlpha(255);

            if (neon) {
                int glowColor = rainbow ? Color.WHITE : baseColor;
                paint.setShadowLayer(dp(13), 0, 0, withAlpha(glowColor, 205));
            }

            if (flash) {
                float pulse = 0.48f + 0.52f * (float) ((Math.sin(nowMs / 170.0) + 1.0) / 2.0);
                paint.setAlpha((int) (255 * pulse));
            }

            if (rainbow && rainbowShader != null) {
                shaderMatrix.reset();
                shaderMatrix.setTranslate(-(nowMs / 4f) % Math.max(1, getWidth()), 0);
                rainbowShader.setLocalMatrix(shaderMatrix);
                paint.setShader(rainbowShader);
            }

            Paint.FontMetrics fm = paint.getFontMetrics();
            float baseline = getHeight() / 2f - (fm.ascent + fm.descent) / 2f;
            canvas.drawText(currentPhrase(), x, baseline, paint);
            paint.setShader(null);
        }

        private void drawLed(Canvas canvas, long nowNs) {
            long nowMs = nowNs / 1_000_000L;
            int step = Math.max(dp(8), 8);
            float originY = getHeight() / 2f - ledBitmap.getHeight() / 2f;

            for (int by = 0; by < ledBitmap.getHeight(); by += step) {
                for (int bx = 0; bx < ledBitmap.getWidth(); bx += step) {
                    int px = ledBitmap.getPixel(
                            Math.min(bx, ledBitmap.getWidth() - 1),
                            Math.min(by, ledBitmap.getHeight() - 1));
                    if (Color.alpha(px) < 45) continue;

                    int color = baseColor;
                    if (rainbow) {
                        float hue = (float) (((bx + nowMs / 5.0)
                                % Math.max(1, getWidth()))
                                / Math.max(1, getWidth()) * 360.0);
                        color = Color.HSVToColor(new float[]{hue, 0.74f, 1f});
                    }

                    ledPaint.setColor(color);
                    ledPaint.setAlpha(255);
                    ledPaint.clearShadowLayer();
                    if (flash) {
                        float pulse = 0.50f + 0.50f * (float) ((Math.sin(nowMs / 150.0) + 1.0) / 2.0);
                        ledPaint.setAlpha((int) (115 + 140 * pulse));
                    }
                    if (neon) {
                        ledPaint.setShadowLayer(dp(5), 0, 0, withAlpha(color, 210));
                    }

                    canvas.drawCircle(
                            x + bx,
                            originY + by,
                            step * 0.31f,
                            ledPaint);
                }
            }
        }

        private int withAlpha(int color, int alpha) {
            return Color.argb(
                    Math.max(0, Math.min(255, alpha)),
                    Color.red(color),
                    Color.green(color),
                    Color.blue(color));
        }

        @Override
        public boolean onTouchEvent(MotionEvent event) {
            return detector.onTouchEvent(event) || super.onTouchEvent(event);
        }
    }
}
