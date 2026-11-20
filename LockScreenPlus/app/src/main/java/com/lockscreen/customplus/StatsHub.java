package com.lockscreen.customplus;

import android.app.ActivityManager;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.os.BatteryManager;
import android.os.Build;
import android.os.Environment;
import android.os.StatFs;
import android.os.SystemClock;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.List;
import java.util.Locale;

/**
 * Данные для экрана «статистика по USB»: батарея, ток, температура, память, сеть и журнал событий.
 * Ничего не пишет на диск и никуда не отправляет: всё живёт в памяти, пока работают обои.
 */
final class StatsHub {
    static final class Snap {
        int level = -1;
        boolean charging;
        int plugged;
        boolean usb;      // телефон подключён по USB (к компьютеру или зарядке от USB)
        boolean usbData;  // есть передача данных (обычно — компьютер)
        float tempC;
        int voltMv;
        int currentMa;
        long etaMs = -1;
        String status = "—";
        String health = "—";
        long ramUsed;
        long ramTotal;
        long storUsed;
        long storTotal;
        String net = "—";
        long uptimeMs;
    }

    static final int HIST = 90;
    static final float[] levelHist = new float[HIST];
    static final float[] tempHist = new float[HIST];
    static final float[] currHist = new float[HIST];
    static int histN = 0;

    private static final int LOG_MAX = 80;
    private static final List<String> LOG = new ArrayList<>();
    private static Snap last;
    private static long histAt;
    private static long sampleAt;
    private static boolean hot;

    private StatsHub() {
    }

    static synchronized void log(String msg) {
        String t = new SimpleDateFormat("HH:mm:ss", Locale.getDefault()).format(new Date());
        LOG.add(t + "  " + msg);
        while (LOG.size() > LOG_MAX) {
            LOG.remove(0);
        }
    }

    static synchronized List<String> lines(int n) {
        int from = Math.max(0, LOG.size() - n);
        return new ArrayList<>(LOG.subList(from, LOG.size()));
    }

    static synchronized Snap last() {
        return last;
    }

    /** Прочитать состояние телефона, записать события в журнал (если что-то изменилось) и вернуть снимок. */
    static synchronized Snap poll(Context c) {
        Context a = c.getApplicationContext();
        Snap s = read(a);
        long now = System.currentTimeMillis();
        Snap p = last;
        if (p == null) {
            log("Панель запущена");
            log(s.usb ? "USB подключён" + (s.usbData ? " (передача данных)" : " (зарядка)") : "USB не подключён");
        } else {
            if (s.usb != p.usb) {
                log(s.usb ? "USB подключён" + (s.usbData ? " (передача данных)" : " (зарядка)") : "USB отключён");
            }
            if (s.level >= 0 && s.level != p.level) {
                log("Заряд " + s.level + "%");
            }
            if (s.charging != p.charging) {
                log(s.charging ? "Зарядка началась" : "Зарядка остановлена");
            }
            if (!s.net.equals(p.net)) {
                log("Сеть: " + s.net);
            }
        }
        boolean nowHot = s.tempC >= 40f;
        if (nowHot != hot) {
            hot = nowHot;
            log(hot ? "Телефон нагрелся: " + fmt1(s.tempC) + " °C" : "Температура в норме: " + fmt1(s.tempC) + " °C");
        }
        if (now - sampleAt >= 60000) {
            sampleAt = now;
            log(s.currentMa + " мА · " + fmt1(s.voltMv / 1000f) + " В · " + fmt1(s.tempC) + " °C");
        }
        if (now - histAt >= 20000 || histN == 0) {
            histAt = now;
            push(levelHist, s.level);
            push(tempHist, s.tempC);
            push(currHist, s.currentMa);
            if (histN < HIST) {
                histN++;
            }
        }
        last = s;
        return s;
    }

    private static void push(float[] a, float v) {
        System.arraycopy(a, 1, a, 0, a.length - 1);
        a[a.length - 1] = v;
    }

    static String fmt1(float v) {
        return String.format(Locale.US, "%.1f", v);
    }

    private static Snap read(Context a) {
        Snap s = new Snap();
        try {
            Intent b = a.registerReceiver(null, new IntentFilter(Intent.ACTION_BATTERY_CHANGED));
            if (b != null) {
                int lv = b.getIntExtra(BatteryManager.EXTRA_LEVEL, -1);
                int sc = b.getIntExtra(BatteryManager.EXTRA_SCALE, 100);
                s.level = (lv >= 0 && sc > 0) ? Math.round(lv * 100f / sc) : -1;
                int st = b.getIntExtra(BatteryManager.EXTRA_STATUS, -1);
                s.charging = st == BatteryManager.BATTERY_STATUS_CHARGING || st == BatteryManager.BATTERY_STATUS_FULL;
                s.status = statusText(st);
                s.plugged = b.getIntExtra(BatteryManager.EXTRA_PLUGGED, 0);
                s.tempC = b.getIntExtra(BatteryManager.EXTRA_TEMPERATURE, 0) / 10f;
                s.voltMv = b.getIntExtra(BatteryManager.EXTRA_VOLTAGE, 0);
                s.health = healthText(b.getIntExtra(BatteryManager.EXTRA_HEALTH, 0));
            }
        } catch (Exception e) {
        }
        try {
            BatteryManager bm = (BatteryManager) a.getSystemService(Context.BATTERY_SERVICE);
            if (bm != null) {
                int cur = Math.abs(bm.getIntProperty(BatteryManager.BATTERY_PROPERTY_CURRENT_NOW));
                // у большинства телефонов ток в микроамперах, у некоторых — уже в миллиамперах
                s.currentMa = cur > 20000 ? cur / 1000 : cur;
                if (Build.VERSION.SDK_INT >= 28) {
                    s.etaMs = bm.computeChargeTimeRemaining();
                }
            }
        } catch (Exception e) {
        }
        try {
            Intent u = a.registerReceiver(null, new IntentFilter("android.hardware.usb.action.USB_STATE"));
            s.usbData = u != null && u.getBooleanExtra("connected", false);
        } catch (Exception e) {
        }
        s.usb = s.usbData || s.plugged == BatteryManager.BATTERY_PLUGGED_USB;
        try {
            ActivityManager am = (ActivityManager) a.getSystemService(Context.ACTIVITY_SERVICE);
            ActivityManager.MemoryInfo mi = new ActivityManager.MemoryInfo();
            am.getMemoryInfo(mi);
            s.ramTotal = mi.totalMem;
            s.ramUsed = mi.totalMem - mi.availMem;
        } catch (Exception e) {
        }
        try {
            StatFs sf = new StatFs(Environment.getDataDirectory().getPath());
            s.storTotal = sf.getTotalBytes();
            s.storUsed = s.storTotal - sf.getAvailableBytes();
        } catch (Exception e) {
        }
        s.net = netName(a);
        s.uptimeMs = SystemClock.elapsedRealtime();
        return s;
    }

    private static String netName(Context a) {
        try {
            ConnectivityManager cm = (ConnectivityManager) a.getSystemService(Context.CONNECTIVITY_SERVICE);
            Network n = cm.getActiveNetwork();
            if (n == null) {
                return "нет сети";
            }
            NetworkCapabilities nc = cm.getNetworkCapabilities(n);
            if (nc == null) {
                return "нет сети";
            }
            if (nc.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)) {
                return "Wi-Fi";
            }
            if (nc.hasTransport(NetworkCapabilities.TRANSPORT_CELLULAR)) {
                return "мобильная сеть";
            }
            if (nc.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET)) {
                return "Ethernet (USB)";
            }
            return "сеть";
        } catch (Exception e) {
            return "—";
        }
    }

    private static String statusText(int st) {
        switch (st) {
            case BatteryManager.BATTERY_STATUS_CHARGING:
                return "Заряжается";
            case BatteryManager.BATTERY_STATUS_DISCHARGING:
                return "Разряжается";
            case BatteryManager.BATTERY_STATUS_FULL:
                return "Заряжен";
            case BatteryManager.BATTERY_STATUS_NOT_CHARGING:
                return "Не заряжается";
            default:
                return "—";
        }
    }

    private static String healthText(int h) {
        switch (h) {
            case BatteryManager.BATTERY_HEALTH_GOOD:
                return "хорошее";
            case BatteryManager.BATTERY_HEALTH_OVERHEAT:
                return "перегрев";
            case BatteryManager.BATTERY_HEALTH_COLD:
                return "холодно";
            case BatteryManager.BATTERY_HEALTH_OVER_VOLTAGE:
                return "перенапряжение";
            case BatteryManager.BATTERY_HEALTH_DEAD:
                return "изношена";
            default:
                return "—";
        }
    }
}
