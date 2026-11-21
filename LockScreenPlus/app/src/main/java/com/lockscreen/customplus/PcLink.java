package com.lockscreen.customplus;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.net.ServerSocket;
import java.net.Socket;
import org.json.JSONObject;

/**
 * Приём данных компьютера. Программа «Lock Screen PC Stats» на Windows раз в секунду шлёт короткий JSON
 * по UDP на этот телефон (через USB-модем), мы его только читаем и показываем. Ничего не отправляем и не сохраняем.
 */
final class PcLink {
    static final int PORT = 47821;      // UDP, через USB-модем
    static final int TCP_PORT = 47822;  // TCP только на этом телефоне: сюда приходит дорожка «отладка по USB» (adb forward)
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
        float cpuClk = Float.NaN;   // МГц
        float gpuW = Float.NaN;
        float gpuClk = Float.NaN;   // МГц
        float vramU = Float.NaN;    // ГБ
        float vramT = Float.NaN;
        float fan = Float.NaN;      // об/мин
        float diskT = Float.NaN;
        float diskU = Float.NaN;    // % занято
        float netD = Float.NaN;     // Мбит/с
        float netU = Float.NaN;
        float fps = Float.NaN;
        float fps1 = Float.NaN;     // «1% low»
        float ft = Float.NaN;       // время кадра, мс
        String game = "";
        String uptime = "";
        long at;
    }

    static final float[] cpuTHist = new float[HIST];
    static final float[] cpuLHist = new float[HIST];
    static final float[] gpuTHist = new float[HIST];
    static final float[] gpuLHist = new float[HIST];
    static final float[] fpsHist = new float[HIST];
    static final float[] ftHist = new float[HIST];
    static final float[] netHist = new float[HIST];
    static final float[] ramHist = new float[HIST];
    static int histN = 0;

    private static volatile Data last;
    private static Thread thread;
    private static Thread tcpThread;
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
        if (tcpThread == null || !tcpThread.isAlive()) {
            tcpThread = new Thread(new Runnable() {
                @Override
                public void run() {
                    tcpLoop();
                }
            }, "pc-link-tcp");
            tcpThread.setDaemon(true);
            tcpThread.start();
        }
    }

    /** Слушаем только адрес самого телефона (127.0.0.1): снаружи сюда не попасть, только через adb forward с компьютера. */
    private static void tcpLoop() {
        ServerSocket ss = null;
        while (true) {
            try {
                if (ss == null || ss.isClosed()) {
                    ss = new ServerSocket();
                    ss.setReuseAddress(true);
                    ss.bind(new java.net.InetSocketAddress(InetAddress.getByName("127.0.0.1"), TCP_PORT));
                }
                Socket c = ss.accept();
                try {
                    c.setSoTimeout(8000);
                    BufferedReader r = new BufferedReader(new InputStreamReader(c.getInputStream(), "UTF-8"), 4096);
                    String line;
                    while ((line = r.readLine()) != null) {
                        if (line.length() > 2000) {
                            continue;
                        }
                        Data d = parse(line);
                        if (d != null) {
                            publish(d);
                        }
                    }
                } catch (Exception e) {
                    // потеряли соединение — ждём следующее
                } finally {
                    try {
                        c.close();
                    } catch (Exception ignored) {
                    }
                }
            } catch (Exception e) {
                try {
                    if (ss != null) {
                        ss.close();
                    }
                } catch (Exception ignored) {
                }
                ss = null;
                try {
                    Thread.sleep(3000);
                } catch (InterruptedException ie) {
                    return;
                }
            }
        }
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
            d.cpuClk = num(o, "cpuClk", 0, 10000);
            d.gpuW = num(o, "gpuW", 0, 1500);
            d.gpuClk = num(o, "gpuClk", 0, 6000);
            d.vramU = num(o, "vramU", 0, 512);
            d.vramT = num(o, "vramT", 0, 512);
            d.fan = num(o, "fan", 0, 30000);
            d.diskT = num(o, "diskT", -20, 150);
            d.diskU = num(o, "diskU", 0, 100);
            d.netD = num(o, "netD", 0, 1000000);
            d.netU = num(o, "netU", 0, 1000000);
            d.fps = num(o, "fps", 0, 2000);
            d.fps1 = num(o, "fps1", 0, 2000);
            d.ft = num(o, "ft", 0, 2000);
            d.game = clean(o.optString("game", ""), 40);
            d.uptime = clean(o.optString("uptime", ""), 24);
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
            push(gpuTHist, d.gpuT);
            push(gpuLHist, d.gpuL);
            push(fpsHist, d.fps);
            push(ftHist, d.ft);
            push(netHist, d.netD);
            push(ramHist, d.ramU);
            if (histN < HIST) {
                histN++;
            }
        }
    }

    /** Выдуманные данные для кнопки «Показать пример» (когда компьютер не подключён). */
    static Data demo() {
        Data d = new Data();
        d.host = "TIOXSIT";
        d.cpuName = "AMD Ryzen 7 5700X";
        d.gpuName = "NVIDIA GeForce RTX 5060";
        d.cpuT = 47;
        d.cpuL = 6;
        d.cpuW = 31;
        d.cpuClk = 4650;
        d.gpuT = 43;
        d.gpuL = 8;
        d.gpuW = 42;
        d.gpuClk = 2100;
        d.vramU = 3.1f;
        d.vramT = 8;
        d.ramU = 9.5f;
        d.ramT = 16;
        d.fan = 1020;
        d.diskT = 38;
        d.diskU = 62;
        d.netD = 12.4f;
        d.netU = 1.4f;
        d.fps = 144;
        d.fps1 = 112;
        d.ft = 6.9f;
        d.game = "Cyberpunk2077.exe";
        d.uptime = "3 ч 12 мин";
        d.at = System.currentTimeMillis();
        if (histN == 0) {
            // немного выдуманной истории, чтобы в примере были видны графики
            for (int i = 0; i < HIST; i++) {
                double t = i / 9.0;
                cpuTHist[i] = (float) (47 + 3 * Math.sin(t));
                cpuLHist[i] = (float) (12 + 8 * Math.sin(t * 1.7) + 4 * Math.sin(t * 4));
                gpuTHist[i] = (float) (43 + 1.5 * Math.sin(t * 0.8));
                gpuLHist[i] = (float) (20 + 10 * Math.sin(t));
                fpsHist[i] = (float) (144 + 10 * Math.sin(t * 2.3) + 5 * Math.sin(t * 5));
                ftHist[i] = (float) (6.9 + 0.8 * Math.sin(t * 2.3));
                netHist[i] = (float) (12 + 6 * Math.sin(t * 1.3));
                ramHist[i] = (float) (9.5 + 0.2 * Math.sin(t));
            }
            histN = HIST;
        }
        return d;
    }

    private static void push(float[] a, float v) {
        System.arraycopy(a, 1, a, 0, a.length - 1);
        a[a.length - 1] = v;
    }
}
