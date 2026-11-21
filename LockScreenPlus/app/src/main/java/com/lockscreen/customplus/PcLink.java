package com.lockscreen.customplus;

import java.net.DatagramPacket;
import java.net.DatagramSocket;
import org.json.JSONObject;

/**
 * Приём данных компьютера. Программа «Lock Screen PC Stats» на Windows раз в секунду шлёт короткий JSON
 * по UDP на этот телефон (через USB-модем), мы его только читаем и показываем. Ничего не отправляем и не сохраняем.
 */
final class PcLink {
    static final int PORT = 47821;
    static final int HIST = 90;

    static final class Data {
        String host = "";
        String cpuName = "";
        String gpuName = "";
        float cpuT = Float.NaN;
        float cpuL = Float.NaN;
        float gpuT = Float.NaN;
        float gpuL = Float.NaN;
        float ramU = Float.NaN;
        float ramT = Float.NaN;
        float cpuW = Float.NaN;
        long at;
    }

    static final float[] cpuTHist = new float[HIST];
    static final float[] cpuLHist = new float[HIST];
    static int histN = 0;

    private static volatile Data last;
    private static Thread thread;
    private static long histAt;

    private PcLink() {
    }

    static synchronized void start() {
        if (thread != null && thread.isAlive()) {
            return;
        }
        thread = new Thread(new Runnable() {
            @Override
            public void run() {
                loop();
            }
        }, "pc-link");
        thread.setDaemon(true);
        thread.start();
    }

    /** Свежие данные (не старше 6 секунд) или null, если компьютер не присылает. */
    static Data fresh() {
        Data d = last;
        if (d != null && System.currentTimeMillis() - d.at < 6000) {
            return d;
        }
        return null;
    }

    private static void loop() {
        DatagramSocket s = null;
        byte[] buf = new byte[2048];
        while (true) {
            try {
                if (s == null || s.isClosed()) {
                    s = new DatagramSocket(null);
                    s.setReuseAddress(true);
                    s.bind(new java.net.InetSocketAddress(PORT));
                    s.setSoTimeout(5000);
                }
                DatagramPacket p = new DatagramPacket(buf, buf.length);
                s.receive(p);
                Data d = parse(new String(p.getData(), 0, p.getLength(), "UTF-8"));
                if (d != null) {
                    publish(d);
                }
            } catch (java.net.SocketTimeoutException e) {
                // тишина — это нормально
            } catch (Exception e) {
                try {
                    if (s != null) {
                        s.close();
                    }
                } catch (Exception ignored) {
                }
                s = null;
                try {
                    Thread.sleep(3000);
                } catch (InterruptedException ie) {
                    return;
                }
            }
        }
    }

    static Data parse(String text) {
        try {
            JSONObject o = new JSONObject(text);
            if (o.optInt("v", 0) != 1) {
                return null;
            }
            Data d = new Data();
            d.host = clean(o.optString("host", ""), 40);
            d.cpuName = clean(o.optString("cpuName", ""), 60);
            d.gpuName = clean(o.optString("gpuName", ""), 60);
            d.cpuT = num(o, "cpuT", -20, 150);
            d.cpuL = num(o, "cpuL", 0, 100);
            d.gpuT = num(o, "gpuT", -20, 150);
            d.gpuL = num(o, "gpuL", 0, 100);
            d.ramU = num(o, "ramU", 0, 4096);
            d.ramT = num(o, "ramT", 0, 4096);
            d.cpuW = num(o, "cpuW", 0, 1000);
            d.at = System.currentTimeMillis();
            return d;
        } catch (Exception e) {
            return null;
        }
    }

    private static float num(JSONObject o, String k, double lo, double hi) {
        if (!o.has(k) || o.isNull(k)) {
            return Float.NaN;
        }
        double v = o.optDouble(k, Double.NaN);
        if (Double.isNaN(v) || v < lo || v > hi) {
            return Float.NaN;
        }
        return (float) v;
    }

    private static String clean(String s, int max) {
        StringBuilder b = new StringBuilder();
        for (int i = 0; i < s.length() && b.length() < max; i++) {
            char ch = s.charAt(i);
            b.append(ch < 32 || ch == 127 ? ' ' : ch);
        }
        return b.toString().trim();
    }

    private static synchronized void publish(Data d) {
        last = d;
        long now = d.at;
        if (now - histAt >= 2000 || histN == 0) {
            histAt = now;
            push(cpuTHist, d.cpuT);
            push(cpuLHist, d.cpuL);
            if (histN < HIST) {
                histN++;
            }
        }
    }

    private static void push(float[] a, float v) {
        System.arraycopy(a, 1, a, 0, a.length - 1);
        a[a.length - 1] = v;
    }
}
