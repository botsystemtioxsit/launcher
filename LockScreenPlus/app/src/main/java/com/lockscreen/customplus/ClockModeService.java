package com.lockscreen.customplus;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.hardware.Sensor;
import android.hardware.SensorEvent;
import android.hardware.SensorEventListener;
import android.hardware.SensorManager;
import android.os.Build;
import android.os.IBinder;
import android.os.SystemClock;
import android.provider.Settings;

/**
 * Следит за положением телефона. Лёг на бок — открывает «Режим часов» поверх экрана блокировки.
 * Окно из фона можно открыть только при разрешении «Поверх других окон» (его включает человек).
 */
public class ClockModeService extends Service implements SensorEventListener {
    private static final String CHANNEL = "clockmode";
    private SensorManager sensors;
    private float sx;
    private float sy;
    private float sz;
    private boolean have;
    private boolean armed = true; // можно открывать: телефон перед этим стоял вертикально/лежал плоско
    private long sideSince;

    static void apply(Context c) {
        Context a = c.getApplicationContext();
        Intent i = new Intent(a, ClockModeService.class);
        if (Store.clockModeOn(a) && Settings.canDrawOverlays(a)) {
            if (Build.VERSION.SDK_INT >= 26) {
                a.startForegroundService(i);
            } else {
                a.startService(i);
            }
        } else {
            a.stopService(i);
        }
    }

    @Override
    public void onCreate() {
        super.onCreate();
        NotificationManager nm = (NotificationManager) getSystemService(Context.NOTIFICATION_SERVICE);
        if (Build.VERSION.SDK_INT >= 26) {
            nm.createNotificationChannel(new NotificationChannel(CHANNEL, "Режим часов", NotificationManager.IMPORTANCE_MIN));
        }
        PendingIntent pi = PendingIntent.getActivity(this, 0, new Intent(this, MainActivity.class), PendingIntent.FLAG_IMMUTABLE);
        Notification n = new Notification.Builder(this, CHANNEL)
                .setSmallIcon(R.drawable.ic_launcher)
                .setContentTitle("Режим часов включён")
                .setContentText("Положите телефон на бок — откроются часы")
                .setContentIntent(pi)
                .setOngoing(true)
                .build();
        if (Build.VERSION.SDK_INT >= 34) {
            startForeground(41, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE);
        } else {
            startForeground(41, n);
        }
        sensors = (SensorManager) getSystemService(Context.SENSOR_SERVICE);
        Sensor a = sensors.getDefaultSensor(Sensor.TYPE_ACCELEROMETER);
        if (a != null) {
            sensors.registerListener(this, a, SensorManager.SENSOR_DELAY_NORMAL);
        }
    }

    @Override
    public int onStartCommand(Intent i, int flags, int id) {
        return START_STICKY;
    }

    @Override
    public void onDestroy() {
        if (sensors != null) {
            sensors.unregisterListener(this);
        }
        super.onDestroy();
    }

    @Override
    public IBinder onBind(Intent i) {
        return null;
    }

    @Override
    public void onSensorChanged(SensorEvent e) {
        float ax = e.values[0];
        float ay = e.values[1];
        float az = e.values[2];
        if (!have) {
            sx = ax;
            sy = ay;
            sz = az;
            have = true;
        } else {
            sx += (ax - sx) * 0.2f;
            sy += (ay - sy) * 0.2f;
            sz += (az - sz) * 0.2f;
        }
        boolean side = Math.abs(sx) > 6.5f && Math.abs(sy) < 4.5f && Math.abs(sz) < 7.5f;
        long now = SystemClock.uptimeMillis();
        if (!side) {
            sideSince = 0;
            if (!ClockModeActivity.showing) {
                armed = true; // убрали с бока — в следующий раз откроем снова
            }
            return;
        }
        if (sideSince == 0) {
            sideSince = now;
            return;
        }
        if (armed && !ClockModeActivity.showing && now - sideSince >= 1000 && Settings.canDrawOverlays(this)) {
            armed = false;
            Intent i = new Intent(this, ClockModeActivity.class);
            i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_NO_ANIMATION);
            try {
                startActivity(i);
            } catch (Exception ex) {
            }
        }
    }

    @Override
    public void onAccuracyChanged(Sensor sensor, int accuracy) {
    }
}
