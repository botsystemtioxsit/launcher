/*
 * Decompiled with CFR 0.152.
 * 
 * Could not load the following classes:
 *  android.content.Context
 *  android.content.SharedPreferences
 *  android.content.SharedPreferences$OnSharedPreferenceChangeListener
 *  android.graphics.Bitmap
 *  android.graphics.BitmapFactory
 *  android.graphics.Canvas
 *  android.hardware.Sensor
 *  android.hardware.SensorEvent
 *  android.hardware.SensorEventListener
 *  android.hardware.SensorManager
 *  android.os.Handler
 *  android.os.Looper
 *  android.service.wallpaper.WallpaperService
 *  android.service.wallpaper.WallpaperService$Engine
 *  android.view.SurfaceHolder
 */
package com.lockscreen.customplus;

import android.content.Context;
import android.content.SharedPreferences;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Canvas;
import android.hardware.Sensor;
import android.hardware.SensorEvent;
import android.hardware.SensorEventListener;
import android.hardware.SensorManager;
import android.os.Handler;
import android.os.Looper;
import android.service.wallpaper.WallpaperService;
import android.view.SurfaceHolder;
import com.lockscreen.customplus.ClockWallpaperService$ClockEngine$$ExternalSyntheticLambda0;
import com.lockscreen.customplus.ClockWallpaperService$ClockEngine$$ExternalSyntheticLambda1;
import com.lockscreen.customplus.ClockWallpaperService_IA;
import com.lockscreen.customplus.Design;
import com.lockscreen.customplus.Fonts;
import com.lockscreen.customplus.Renderer;
import com.lockscreen.customplus.Store;

public class ClockWallpaperService
extends WallpaperService {
    public WallpaperService.Engine onCreateEngine() {
        return new ClockEngine(this, null);
    }

    private class ClockEngine
    extends WallpaperService.Engine
    implements SensorEventListener,
    SharedPreferences.OnSharedPreferenceChangeListener {
        private Sensor accel;
        private float baseY;
        private Bitmap bg;
        private Bitmap cut;
        private Design design;
        private String fetchedText;
        private volatile boolean fetching;
        private volatile boolean fetchingWeather;
        private final Handler handler;
        private boolean haveBase;
        private long imgStamp;
        private long lastAttempt;
        private long lastWeatherAttempt;
        private boolean needReload;
        private final Renderer renderer;
        private boolean sensorOn;
        private SensorManager sensors;
        private SharedPreferences sp;
        private float targetX;
        private float targetY;
        final ClockWallpaperService this$0;
        private final Runnable tick;
        private float tiltX;
        private float tiltY;
        private boolean visible;

        public static /* synthetic */ void $r8$lambda$k_lBZIMery2FfKQfxpCRVd50Wac(ClockEngine clockEngine, double d, double d2) {
            clockEngine.lambda$maybeFetchWeather$1(d, d2);
        }

        public static /* synthetic */ void $r8$lambda$mzugbQ_fPEaWq5sE5fWgWuowmCc(ClockEngine clockEngine, String string2) {
            clockEngine.lambda$maybeFetch$0(string2);
        }

        private ClockEngine(ClockWallpaperService clockWallpaperService) {
            this.this$0 = clockWallpaperService;
            super((WallpaperService)clockWallpaperService);
            this.handler = new Handler(Looper.getMainLooper());
            this.renderer = new Renderer();
            this.design = new Design();
            this.needReload = true;
            this.imgStamp = -1L;
            this.fetchedText = "";
            this.tick = new Runnable(this){
                final ClockEngine this$1;
                {
                    this.this$1 = clockEngine;
                }

                @Override
                public void run() {
                    this.this$1.draw();
                    this.this$1.maybeFetch();
                    this.this$1.maybeFetchWeather();
                    this.this$1.scheduleNext();
                }
            };
        }

        /* synthetic */ ClockEngine(ClockWallpaperService clockWallpaperService, ClockWallpaperService_IA clockWallpaperService_IA) {
            this(clockWallpaperService);
        }

        private float clamp(float f) {
            return Math.max(-1.0f, Math.min(1.0f, f));
        }

        /*
         * Loose catch block
         * Enabled aggressive block sorting
         * Enabled unnecessary exception pruning
         * Enabled aggressive exception aggregation
         */
        private void draw() {
            if (this.needReload) {
                this.reload();
            }
            SurfaceHolder surfaceHolder = this.getSurfaceHolder();
            Canvas canvas = null;
            Canvas canvas2 = null;
            Canvas canvas3 = surfaceHolder.lockCanvas();
            if (canvas3 != null) {
                canvas2 = canvas3;
                canvas = canvas3;
                this.tiltX += (this.targetX - this.tiltX) * 0.15f;
                canvas2 = canvas3;
                canvas = canvas3;
                this.tiltY += (this.targetY - this.tiltY) * 0.15f;
                canvas2 = canvas3;
                canvas = canvas3;
                this.renderer.render(canvas3, canvas3.getWidth(), canvas3.getHeight(), this.design, this.bg, this.cut, this.tiltX, this.tiltY, System.currentTimeMillis(), this.fetchedText, Store.batteryText((Context)this.this$0), true);
            }
            if (canvas3 == null) return;
            surfaceHolder.unlockCanvasAndPost(canvas3);
            return;
            {
                catch (Exception exception) {
                    return;
                }
            }
            catch (Throwable throwable) {
                if (canvas2 == null) throw throwable;
                try {
                    surfaceHolder.unlockCanvasAndPost(canvas2);
                    throw throwable;
                }
                catch (Exception exception) {
                    // empty catch block
                }
                throw throwable;
            }
            catch (Exception exception) {
                if (canvas == null) return;
                surfaceHolder.unlockCanvasAndPost(canvas);
                return;
            }
        }

        private /* synthetic */ void lambda$maybeFetch$0(String string2) {
            try {
                string2 = Store.fetchText(string2);
                this.sp.edit().putString("fetchedText", string2).putLong("fetchedAt", System.currentTimeMillis()).apply();
            }
            catch (Throwable throwable) {
                this.fetching = false;
                throw throwable;
            }
            catch (Exception exception) {
                // empty catch block
            }
            this.fetching = false;
        }

        private /* synthetic */ void lambda$maybeFetchWeather$1(double d, double d2) {
            try {
                Store.saveWeather((Context)this.this$0, Store.fetchWeather(d, d2));
            }
            catch (Throwable throwable) {
                this.fetchingWeather = false;
                throw throwable;
            }
            catch (Exception exception) {
                // empty catch block
            }
            this.fetchingWeather = false;
        }

        private void maybeFetch() {
            if (this.sp != null && !this.fetching) {
                Design.Layer layer = this.design.webLayer();
                if (layer == null) {
                    return;
                }
                String string2 = layer.url.trim();
                long l = System.currentTimeMillis();
                long l2 = Math.max(1, layer.refreshMin);
                if (l - this.sp.getLong("fetchedAt", 0L) >= l2 * 60000L && l - this.lastAttempt >= 120000L) {
                    this.lastAttempt = l;
                    this.fetching = true;
                    new Thread(new ClockWallpaperService$ClockEngine$$ExternalSyntheticLambda1(this, string2)).start();
                    return;
                }
                return;
            }
        }

        private void maybeFetchWeather() {
            if (this.sp != null && !this.fetchingWeather && this.design.weatherAuto) {
                double d = this.design.weatherLat;
                double d2 = this.design.weatherLon;
                if (!Double.isNaN(d) && !Double.isNaN(d2)) {
                    long l = System.currentTimeMillis();
                    if (l - this.sp.getLong("weatherAt", 0L) >= 1800000L && l - this.lastWeatherAttempt >= 120000L) {
                        this.lastWeatherAttempt = l;
                        this.fetchingWeather = true;
                        new Thread(new ClockWallpaperService$ClockEngine$$ExternalSyntheticLambda0(this, d, d2)).start();
                        return;
                    }
                    return;
                }
                return;
            }
        }

        private void reload() {
            this.needReload = false;
            this.design = Design.load((Context)this.this$0);
            this.fetchedText = this.sp.getString("fetchedText", "");
            long l = this.sp.getLong("imgStamp", 0L);
            if (l != this.imgStamp) {
                this.imgStamp = l;
                if (this.bg != null) {
                    this.bg.recycle();
                }
                if (this.cut != null) {
                    this.cut.recycle();
                }
                this.bg = null;
                this.cut = null;
                try {
                    if (Store.hasBackground((Context)this.this$0)) {
                        this.bg = BitmapFactory.decodeFile((String)Store.bgFile((Context)this.this$0).getPath());
                        if (Store.hasCut((Context)this.this$0)) {
                            this.cut = BitmapFactory.decodeFile((String)Store.cutFile((Context)this.this$0).getPath());
                        }
                    }
                }
                catch (Throwable throwable) {
                    // empty catch block
                }
            }
            this.updateSensor();
        }

        private void scheduleNext() {
            if (!this.visible) {
                return;
            }
            long l = this.design.parallax && this.sensorOn || this.design.animated() ? 33L : 60000L - System.currentTimeMillis() % 60000L + 50L;
            this.handler.postDelayed(this.tick, l);
        }

        private void stopSensor() {
            if (this.sensorOn && this.sensors != null) {
                this.sensors.unregisterListener((SensorEventListener)this);
            }
            this.sensorOn = false;
        }

        private void updateSensor() {
            if (this.visible && this.design.parallax && this.accel != null) {
                if (!this.sensorOn) {
                    this.sensors.registerListener((SensorEventListener)this, this.accel, 1);
                    this.sensorOn = true;
                }
            } else {
                this.stopSensor();
            }
        }

        public void onAccuracyChanged(Sensor sensor, int n) {
        }

        public void onCreate(SurfaceHolder object) {
            super.onCreate(object);
            Fonts.init((Context)this.this$0);
            this.sp = Store.prefs((Context)this.this$0);
            this.sp.registerOnSharedPreferenceChangeListener((SharedPreferences.OnSharedPreferenceChangeListener)this);
            this.sensors = (SensorManager)this.this$0.getSystemService("sensor");
            object = this.sensors != null ? this.sensors.getDefaultSensor(1) : null;
            this.accel = object;
        }

        public void onDestroy() {
            this.visible = false;
            this.handler.removeCallbacks(this.tick);
            this.stopSensor();
            if (this.sp != null) {
                this.sp.unregisterOnSharedPreferenceChangeListener((SharedPreferences.OnSharedPreferenceChangeListener)this);
            }
            if (this.bg != null) {
                this.bg.recycle();
            }
            if (this.cut != null) {
                this.cut.recycle();
            }
            this.bg = null;
            this.cut = null;
            super.onDestroy();
        }

        public void onSensorChanged(SensorEvent sensorEvent) {
            float f = sensorEvent.values[0];
            float f2 = sensorEvent.values[1];
            if (!this.haveBase) {
                this.baseY = f2;
                this.haveBase = true;
            }
            this.baseY += (f2 - this.baseY) * 0.003f;
            this.targetX = this.clamp(-f / 6.0f);
            this.targetY = this.clamp((f2 - this.baseY) / 6.0f);
        }

        public void onSharedPreferenceChanged(SharedPreferences sharedPreferences, String string2) {
            this.needReload = true;
            if (this.visible) {
                this.handler.removeCallbacks(this.tick);
                this.handler.post(this.tick);
            }
        }

        public void onSurfaceChanged(SurfaceHolder surfaceHolder, int n, int n2, int n3) {
            super.onSurfaceChanged(surfaceHolder, n, n2, n3);
            this.draw();
        }

        public void onSurfaceDestroyed(SurfaceHolder surfaceHolder) {
            this.visible = false;
            this.handler.removeCallbacks(this.tick);
            this.stopSensor();
            super.onSurfaceDestroyed(surfaceHolder);
        }

        public void onVisibilityChanged(boolean bl) {
            this.visible = bl;
            this.handler.removeCallbacks(this.tick);
            if (bl) {
                if (this.needReload) {
                    this.reload();
                }
                this.updateSensor();
                this.handler.post(this.tick);
            } else {
                this.stopSensor();
            }
        }
    }
}

