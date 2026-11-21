package com.lockscreen.customplus;

import android.app.Activity;
import android.content.Context;
import android.content.pm.ActivityInfo;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Canvas;
import android.hardware.Sensor;
import android.hardware.SensorEvent;
import android.hardware.SensorEventListener;
import android.hardware.SensorManager;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.view.MotionEvent;
import android.view.View;
import android.view.WindowManager;
import androidx.core.view.WindowCompat;
import androidx.core.view.WindowInsetsCompat;
import androidx.core.view.WindowInsetsControllerCompat;

/**
 * «Режим часов»: окно приложения поверх экрана блокировки на весь экран (без меню Samsung), экран не гаснет.
 * Закрывается нажатием на экран или когда телефон подняли вертикально.
 */
public class ClockModeActivity extends Activity implements SensorEventListener {
    static volatile boolean showing;

    private ClockView view;
    private SensorManager sensors;
    private long uprightSince;

    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        showing = true;
        if (Build.VERSION.SDK_INT >= 27) {
            setShowWhenLocked(true);
            setTurnScreenOn(true);
        } else {
            getWindow().addFlags(WindowManager.LayoutParams.FLAG_SHOW_WHEN_LOCKED | WindowManager.LayoutParams.FLAG_TURN_SCREEN_ON);
        }
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        setRequestedOrientation(ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE);
        if (Build.VERSION.SDK_INT >= 28) {
            getWindow().getAttributes().layoutInDisplayCutoutMode = WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES;
        }
        WindowCompat.setDecorFitsSystemWindows(getWindow(), false);
        Fonts.init(this);
        PcLink.start();
        view = new ClockView(this);
        setContentView(view);
        hideBars();
        sensors = (SensorManager) getSystemService(Context.SENSOR_SERVICE);
    }

    private void hideBars() {
        WindowInsetsControllerCompat ic = WindowCompat.getInsetsController(getWindow(), getWindow().getDecorView());
        ic.setSystemBarsBehavior(WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
        ic.hide(WindowInsetsCompat.Type.systemBars());
    }

    @Override
    public void onWindowFocusChanged(boolean hasFocus) {
        super.onWindowFocusChanged(hasFocus);
        if (hasFocus) {
            hideBars();
        }
    }

    @Override
    protected void onResume() {
        super.onResume();
        view.start();
        if (sensors != null && !getIntent().getBooleanExtra("demo", false)) {
            Sensor a = sensors.getDefaultSensor(Sensor.TYPE_ACCELEROMETER);
            if (a != null) {
                sensors.registerListener(this, a, SensorManager.SENSOR_DELAY_NORMAL);
            }
        }
    }

    @Override
    protected void onPause() {
        view.stop();
        if (sensors != null) {
            sensors.unregisterListener(this);
        }
        super.onPause();
    }

    @Override
    protected void onDestroy() {
        showing = false;
        view.release();
        super.onDestroy();
    }

    @Override
    public void onSensorChanged(SensorEvent e) {
        // вертикально: гравитация вдоль длинной стороны экрана (в режиме «пейзаж» это ось X)
        float ax = Math.abs(e.values[0]);
        float ay = Math.abs(e.values[1]);
        boolean upright = ay > 7.0f && ax < 4.0f;
        long now = SystemClock.uptimeMillis();
        if (!upright) {
            uprightSince = 0;
        } else if (uprightSince == 0) {
            uprightSince = now;
        } else if (now - uprightSince > 1200) {
            finish();
        }
    }

    @Override
    public void onAccuracyChanged(Sensor sensor, int accuracy) {
    }

    private static final class ClockView extends View {
        private final Renderer renderer = new Renderer();
        private final Dashboard dash = new Dashboard();
        private final PcDash pcDash = new PcDash();
        private final PcStyles pcStyles = new PcStyles();
        private boolean demo;
        private final Handler handler = new Handler(Looper.getMainLooper());
        private final Activity act;
        private Design design;
        private Bitmap bg;
        private StatsHub.Snap snap;
        private long lastPoll;
        private boolean running;

        private final Runnable tick = new Runnable() {
            @Override
            public void run() {
                if (!running) {
                    return;
                }
                invalidate();
                handler.postDelayed(this, 500);
            }
        };

        ClockView(Activity a) {
            super(a);
            act = a;
            design = Design.load(a, 1);
            demo = a.getIntent() != null && a.getIntent().getBooleanExtra("demo", false);
            try {
                if (Store.hasBackground(a)) {
                    bg = BitmapFactory.decodeFile(Store.bgFile(a).getPath());
                }
            } catch (Throwable t) {
            }
        }

        void start() {
            running = true;
            handler.removeCallbacks(tick);
            handler.post(tick);
        }

        void stop() {
            running = false;
            handler.removeCallbacks(tick);
        }

        void release() {
            stop();
            if (bg != null) {
                bg.recycle();
                bg = null;
            }
        }

        @Override
        protected void onDraw(Canvas c) {
            long now = System.currentTimeMillis();
            int w = getWidth();
            int h = getHeight();
            if (Store.usbOn(act)) {
                if (snap == null || now - lastPoll >= 2000) {
                    lastPoll = now;
                    snap = StatsHub.poll(act);
                }
            } else {
                snap = null;
            }
            PcLink.Data pc = demo ? PcLink.demo() : PcLink.fresh();
            if (pc != null) {
                if (w >= h * 1.3f) {
                    pcStyles.render(c, w, h, pc, Store.pcStyle(act), now);
                } else {
                    pcDash.render(c, w, h, pc, now);
                }
            } else if (snap != null && snap.usb) {
                dash.render(c, w, h, snap, now);
            } else {
                renderer.render(c, w, h, design, bg, null, 0.0f, 0.0f, now, "", Store.batteryText(act), true);
            }
        }

        @Override
        public boolean onTouchEvent(MotionEvent e) {
            if (e.getAction() == MotionEvent.ACTION_UP) {
                act.finish();
            }
            return true;
        }
    }
}
