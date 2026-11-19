package com.lockscreen.customplus;

import android.content.Context;
import android.content.SharedPreferences;
import androidx.core.view.ViewCompat;
import com.google.android.gms.common.internal.ImagesContract;
import com.google.mlkit.common.MlKitException;
import java.util.ArrayList;
import java.util.List;
import org.json.JSONArray;
import org.json.JSONObject;

/* loaded from: classes3.dex */
final class Design {
    static final int UNSET = -9999;
    int[] sys;
    static final String[] STYLE_NAMES = {"Крупные", "Двухцветные", "Контур", "Строка", "Строка двухцветная", "Контурная строка", "Аналоговые", "Моно", "Узкие", "С засечками", "Техно", "Плакат", "Пиксели", "Мягкие", "Высокие узкие", "Градиент"};
    static final String[] DATE_FORMATS = {"EEEE, d MMMM", "d MMMM", "EEE, d MMM", "dd.MM.yyyy", "yyyy-MM-dd", "MMMM d"};
    static final String[] DATE_NAMES = {"Воскресенье, 25 октября", "25 октября", "Вс, 25 окт", "25.10.2026", "2026-10-25", "Октябрь 25"};
    static final String[] SEPARATORS = {":", ".", " ", ""};
    static final String[] SEPARATOR_NAMES = {"11:11", "11.11", "11 11", "1111"};
    int bgMode = 0;
    int grad1 = -14997707;
    int grad2 = -11830879;
    boolean gradAnim = true;
    int dim = 0;
    int hue = 0;
    int sat = 100;
    boolean clockShow = true;
    int clockStyle = 0;
    boolean outline = false;
    boolean twoTone = false;
    boolean gradText = false;
    int gradAngle = 90;
    boolean stroke = false;
    int strokeW = 6;
    int strokeColor = ViewCompat.MEASURED_STATE_MASK;
    boolean shadow = true;
    int clockColor = -1;
    int clockColor2 = -1754827;
    FontSpec font = new FontSpec();
    int clockSize = 100;
    float clockX = 0.5f;
    float clockY = 0.3f;
    boolean use24 = true;
    int separator = 0;
    int letterSpacing = 0;
    int lineGap = 40;
    boolean clockOver = false;
    Xf clockXf = new Xf();
    int textColor = -1;
    boolean dateShow = true;
    float dateX = 0.5f;
    float dateY = 0.5f;
    int dateSize = 100;
    int dateFmt = 0;
    FontSpec dateFont = new FontSpec();
    Xf dateXf = new Xf();
    boolean batShow = false;
    float batX = 0.5f;
    float batY = 0.55f;
    int batSize = 100;
    Xf batXf = new Xf();
    final List<Layer> layers = new ArrayList();
    final List<Sticker> stickers = new ArrayList();
    boolean parallax = true;
    int parallaxStrength = 50;
    boolean parallaxText = false;
    int particles = 0;
    int particleCount = 60;
    int pSpeed = 100;
    int pSize = 100;
    int pAlpha = 100;
    int pColor = 0;
    boolean autoColors = false;
    boolean weatherAuto = false;
    String weatherCity = "";
    double weatherLat = Double.NaN;
    double weatherLon = Double.NaN;
    int weatherCode = -1;

    Design() {
    }

    static final class FontSpec {
        int cnt;
        int hgt;
        int hstem;
        String id;
        int slnt;
        int stem;
        int wdth;
        int wght;

        FontSpec() {
            this.id = "sys:0";
            this.wght = Design.UNSET;
            this.wdth = Design.UNSET;
            this.slnt = Design.UNSET;
            this.hgt = Design.UNSET;
            this.stem = Design.UNSET;
            this.hstem = Design.UNSET;
            this.cnt = Design.UNSET;
        }

        FontSpec(String id) {
            this.id = "sys:0";
            this.wght = Design.UNSET;
            this.wdth = Design.UNSET;
            this.slnt = Design.UNSET;
            this.hgt = Design.UNSET;
            this.stem = Design.UNSET;
            this.hstem = Design.UNSET;
            this.cnt = Design.UNSET;
            this.id = id;
        }

        FontSpec copy() {
            FontSpec f = new FontSpec(this.id);
            f.wght = this.wght;
            f.wdth = this.wdth;
            f.slnt = this.slnt;
            f.hgt = this.hgt;
            f.stem = this.stem;
            f.hstem = this.hstem;
            f.cnt = this.cnt;
            return f;
        }

        void resetAxes() {
            this.cnt = Design.UNSET;
            this.hstem = Design.UNSET;
            this.stem = Design.UNSET;
            this.hgt = Design.UNSET;
            this.slnt = Design.UNSET;
            this.wdth = Design.UNSET;
            this.wght = Design.UNSET;
        }

        boolean hasAxes() {
            return (this.wght == Design.UNSET && this.wdth == Design.UNSET && this.slnt == Design.UNSET && this.hgt == Design.UNSET && this.stem == Design.UNSET && this.hstem == Design.UNSET && this.cnt == Design.UNSET) ? false : true;
        }

        String variation() {
            StringBuilder b = new StringBuilder();
            add(b, "wght", this.wght);
            add(b, "wdth", this.wdth);
            add(b, "slnt", this.slnt);
            add(b, "YTFI", this.hgt);
            add(b, "XOPQ", this.stem);
            add(b, "YOPQ", this.hstem);
            add(b, "XTRA", this.cnt);
            return b.toString();
        }

        private static void add(StringBuilder b, String tag, int v) {
            if (v == Design.UNSET) {
                return;
            }
            if (b.length() > 0) {
                b.append(", ");
            }
            b.append('\'').append(tag).append("' ").append(v);
        }

        JSONObject toJson() throws Exception {
            JSONObject o = new JSONObject().put("id", this.id);
            if (this.wght != Design.UNSET) {
                o.put("wght", this.wght);
            }
            if (this.wdth != Design.UNSET) {
                o.put("wdth", this.wdth);
            }
            if (this.slnt != Design.UNSET) {
                o.put("slnt", this.slnt);
            }
            if (this.hgt != Design.UNSET) {
                o.put("hgt", this.hgt);
            }
            if (this.stem != Design.UNSET) {
                o.put("stem", this.stem);
            }
            if (this.hstem != Design.UNSET) {
                o.put("hstem", this.hstem);
            }
            if (this.cnt != Design.UNSET) {
                o.put("cnt", this.cnt);
            }
            return o;
        }

        static FontSpec fromJson(JSONObject o, FontSpec def) {
            if (o == null) {
                return def;
            }
            FontSpec f = new FontSpec(o.optString("id", def.id));
            f.wght = o.optInt("wght", Design.UNSET);
            f.wdth = o.optInt("wdth", Design.UNSET);
            f.slnt = o.optInt("slnt", Design.UNSET);
            f.hgt = o.optInt("hgt", Design.UNSET);
            f.stem = o.optInt("stem", Design.UNSET);
            f.hstem = o.optInt("hstem", Design.UNSET);
            f.cnt = o.optInt("cnt", Design.UNSET);
            return f;
        }
    }

    static final class Xf {
        int rot;
        int rx;
        int ry;
        int skew;
        int sx = 100;
        int sy = 100;

        Xf() {
        }

        Xf copy() {
            Xf x = new Xf();
            x.rot = this.rot;
            x.rx = this.rx;
            x.ry = this.ry;
            x.skew = this.skew;
            x.sx = this.sx;
            x.sy = this.sy;
            return x;
        }

        boolean identity() {
            return this.rot == 0 && this.rx == 0 && this.ry == 0 && this.skew == 0 && this.sx == 100 && this.sy == 100;
        }

        void reset() {
            this.skew = 0;
            this.ry = 0;
            this.rx = 0;
            this.rot = 0;
            this.sy = 100;
            this.sx = 100;
        }

        JSONObject toJson() throws Exception {
            return new JSONObject().put("rot", this.rot).put("rx", this.rx).put("ry", this.ry).put("skew", this.skew).put("sx", this.sx).put("sy", this.sy);
        }

        static Xf fromJson(JSONObject o) {
            Xf x = new Xf();
            if (o == null) {
                return x;
            }
            x.rot = o.optInt("rot", 0);
            x.rx = o.optInt("rx", 0);
            x.ry = o.optInt("ry", 0);
            x.skew = o.optInt("skew", 0);
            x.sx = o.optInt("sx", 100);
            x.sy = o.optInt("sy", 100);
            return x;
        }
    }

    static final class Layer {
        boolean web;
        String text = "";
        String url = "";
        int refreshMin = 30;
        float x = 0.5f;
        float y = 0.62f;
        int size = 100;
        int color = 0;
        FontSpec font = new FontSpec();
        Xf xf = new Xf();

        Layer() {
        }

        Layer copy() {
            Layer l = new Layer();
            l.web = this.web;
            l.text = this.text;
            l.url = this.url;
            l.refreshMin = this.refreshMin;
            l.x = Math.min(0.96f, this.x + 0.04f);
            l.y = Math.min(0.97f, this.y + 0.04f);
            l.size = this.size;
            l.color = this.color;
            l.font = this.font.copy();
            l.xf = this.xf.copy();
            return l;
        }
    }

    static final class Sticker {
        String s;
        float x;
        float y;
        int size = 100;
        Xf xf = new Xf();

        Sticker(String s, float x, float y) {
            this.s = s;
            this.x = x;
            this.y = y;
        }
    }

    int clockCol() {
        return (!this.autoColors || this.sys == null) ? this.clockColor : this.sys[0];
    }

    int clockCol2() {
        return (!this.autoColors || this.sys == null) ? this.clockColor2 : this.sys[1];
    }

    int textCol() {
        return (!this.autoColors || this.sys == null) ? this.textColor : this.sys[0];
    }

    int grad1Col() {
        return (!this.autoColors || this.sys == null) ? this.grad1 : this.sys[2];
    }

    int grad2Col() {
        return (!this.autoColors || this.sys == null) ? this.grad2 : this.sys[3];
    }

    int effKind() {
        if (!this.weatherAuto) {
            return this.particles;
        }
        int c = this.weatherCode;
        if (c >= 51 && c <= 67) {
            return 2;
        }
        if (c >= 80 && c <= 82) {
            return 2;
        }
        if (c >= 95 && c <= 99) {
            return 2;
        }
        if ((c >= 71 && c <= 77) || c == 85 || c == 86) {
            return 1;
        }
        return 0;
    }

    int effCount() {
        if (!this.weatherAuto) {
            return this.particleCount;
        }
        int c = this.weatherCode;
        if (c == 51 || c == 56) {
            return 30;
        }
        if (c == 53) {
            return 45;
        }
        if (c == 55 || c == 57) {
            return 60;
        }
        if (c == 61 || c == 80) {
            return 70;
        }
        if (c == 63 || c == 66 || c == 81) {
            return 100;
        }
        if (c == 65 || c == 67 || c == 82) {
            return 150;
        }
        if (c >= 95 && c <= 99) {
            return 160;
        }
        if (c == 71 || c == 85) {
            return 50;
        }
        if (c == 73 || c == 77) {
            return 90;
        }
        if (c == 75 || c == 86) {
            return 140;
        }
        return 0;
    }

    boolean stormy() {
        return this.weatherAuto && this.weatherCode >= 95 && this.weatherCode <= 99;
    }

    void attachRuntime(Context c) {
        this.sys = SystemColors.auto(c);
        SharedPreferences p = Store.prefs(c);
        long age = System.currentTimeMillis() - p.getLong("weatherAt", 0L);
        this.weatherCode = age < 14400000 ? p.getInt("weatherCode", -1) : -1;
    }

    Design copyRuntime(Design from) {
        this.sys = from.sys;
        this.weatherCode = from.weatherCode;
        return this;
    }

    boolean animated() {
        if (effKind() <= 0) {
            return this.bgMode == 1 && this.gradAnim;
        }
        return true;
    }

    static void applyStyle(Design d, int i) {
        d.clockShow = true;
        d.outline = false;
        d.twoTone = false;
        d.gradText = false;
        d.font = new FontSpec();
        switch (i) {
            case 0:
                d.clockStyle = 0;
                break;
            case 1:
                d.clockStyle = 0;
                d.font.id = "sys:2";
                d.twoTone = true;
                break;
            case 2:
                d.clockStyle = 0;
                d.font.id = "sys:2";
                d.outline = true;
                break;
            case 3:
                d.clockStyle = 1;
                break;
            case 4:
                d.clockStyle = 1;
                d.font.id = "sys:2";
                d.twoTone = true;
                break;
            case 5:
                d.clockStyle = 1;
                d.font.id = "sys:2";
                d.outline = true;
                break;
            case 6:
                d.clockStyle = 2;
                break;
            case 7:
                d.clockStyle = 1;
                d.font.id = "sys:5";
                break;
            case 8:
                d.clockStyle = 0;
                d.font.id = "sys:3";
                break;
            case 9:
                d.clockStyle = 1;
                d.font.id = "sys:4";
                break;
            case 10:
                d.clockStyle = 1;
                d.font.id = "asset:Orbitron.ttf";
                d.twoTone = true;
                break;
            case 11:
                d.clockStyle = 0;
                d.font.id = "asset:Anton.ttf";
                d.twoTone = true;
                break;
            case 12:
                d.clockStyle = 1;
                d.font.id = "asset:PressStart2P.ttf";
                break;
            case 13:
                d.clockStyle = 0;
                d.font.id = "asset:Comfortaa.ttf";
                break;
            case 14:
                d.clockStyle = 0;
                d.font.id = "asset:RobotoFlex.ttf";
                d.font.wght = 800;
                d.font.wdth = 28;
                d.font.hgt = 788;
                break;
            default:
                d.clockStyle = 0;
                d.font.id = "sys:2";
                d.gradText = true;
                break;
        }
        if ((d.twoTone || d.gradText) && d.clockColor2 == d.clockColor) {
            d.clockColor2 = -1754827;
        }
    }

    static boolean matchesStyle(Design d, int i) {
        Design t = d.copy();
        applyStyle(t, i);
        return t.clockStyle == d.clockStyle && t.outline == d.outline && t.twoTone == d.twoTone && t.gradText == d.gradText && (t.clockStyle == 2 || t.font.id.equals(d.font.id));
    }

    List<String[]> elements() {
        List<String[]> out = new ArrayList<>();
        out.add(new String[]{"clock", "Часы"});
        out.add(new String[]{"date", "Дата"});
        out.add(new String[]{"bat", "Батарея"});
        for (int i = 0; i < this.layers.size(); i++) {
            Layer l = this.layers.get(i);
            String name = l.web ? "Из интернета" : l.text.trim().isEmpty() ? "Текст" : shorten(l.text.trim());
            out.add(new String[]{"tl" + i, name});
        }
        for (int i2 = 0; i2 < this.stickers.size(); i2++) {
            out.add(new String[]{"st" + i2, this.stickers.get(i2).s});
        }
        return out;
    }

    private static String shorten(String s) {
        return s.length() > 12 ? s.substring(0, 12) + "…" : s;
    }

    /* JADX WARN: Can't fix incorrect switch cases order, some code will duplicate */
    float[] pos(String id) {
        char c;
        if (id == null) {
            return null;
        }
        switch (id.hashCode()) {
            case 97301:
                if (id.equals("bat")) {
                    c = 2;
                    break;
                }
                c = 65535;
                break;
            case 3076014:
                if (id.equals("date")) {
                    c = 1;
                    break;
                }
                c = 65535;
                break;
            case 94755854:
                if (id.equals("clock")) {
                    c = 0;
                    break;
                }
                c = 65535;
                break;
            default:
                c = 65535;
                break;
        }
        switch (c) {
            case 0:
                return new float[]{this.clockX, this.clockY};
            case 1:
                return new float[]{this.dateX, this.dateY};
            case 2:
                return new float[]{this.batX, this.batY};
            default:
                Layer l = layer(id);
                if (l != null) {
                    return new float[]{l.x, l.y};
                }
                Sticker s = sticker(id);
                if (s == null) {
                    return null;
                }
                return new float[]{s.x, s.y};
        }
    }

    /* JADX WARN: Can't fix incorrect switch cases order, some code will duplicate */
    void setPos(String id, float x, float y) {
        char c;
        if (id == null) {
        }
        switch (id.hashCode()) {
            case 97301:
                if (id.equals("bat")) {
                    c = 2;
                    break;
                }
                c = 65535;
                break;
            case 3076014:
                if (id.equals("date")) {
                    c = 1;
                    break;
                }
                c = 65535;
                break;
            case 94755854:
                if (id.equals("clock")) {
                    c = 0;
                    break;
                }
                c = 65535;
                break;
            default:
                c = 65535;
                break;
        }
        switch (c) {
            case 0:
                this.clockX = x;
                this.clockY = y;
                break;
            case 1:
                this.dateX = x;
                this.dateY = y;
                break;
            case 2:
                this.batX = x;
                this.batY = y;
                break;
            default:
                Layer l = layer(id);
                if (l != null) {
                    l.x = x;
                    l.y = y;
                    break;
                } else {
                    Sticker s = sticker(id);
                    if (s != null) {
                        s.x = x;
                        s.y = y;
                        break;
                    }
                }
                break;
        }
    }

    /* JADX WARN: Can't fix incorrect switch cases order, some code will duplicate */
    Xf xf(String id) {
        char c;
        if (id == null) {
            return null;
        }
        switch (id.hashCode()) {
            case 97301:
                if (id.equals("bat")) {
                    c = 2;
                    break;
                }
                c = 65535;
                break;
            case 3076014:
                if (id.equals("date")) {
                    c = 1;
                    break;
                }
                c = 65535;
                break;
            case 94755854:
                if (id.equals("clock")) {
                    c = 0;
                    break;
                }
                c = 65535;
                break;
            default:
                c = 65535;
                break;
        }
        switch (c) {
            case 0:
                return this.clockXf;
            case 1:
                return this.dateXf;
            case 2:
                return this.batXf;
            default:
                Layer l = layer(id);
                if (l != null) {
                    return l.xf;
                }
                Sticker s = sticker(id);
                if (s == null) {
                    return null;
                }
                return s.xf;
        }
    }

    /* JADX WARN: Can't fix incorrect switch cases order, some code will duplicate */
    int size(String id) {
        char c;
        if (id == null) {
            return 100;
        }
        switch (id.hashCode()) {
            case 97301:
                if (id.equals("bat")) {
                    c = 2;
                    break;
                }
                c = 65535;
                break;
            case 3076014:
                if (id.equals("date")) {
                    c = 1;
                    break;
                }
                c = 65535;
                break;
            case 94755854:
                if (id.equals("clock")) {
                    c = 0;
                    break;
                }
                c = 65535;
                break;
            default:
                c = 65535;
                break;
        }
        switch (c) {
            case 0:
                return this.clockSize;
            case 1:
                return this.dateSize;
            case 2:
                return this.batSize;
            default:
                Layer l = layer(id);
                if (l != null) {
                    return l.size;
                }
                Sticker s = sticker(id);
                if (s == null) {
                    return 100;
                }
                return s.size;
        }
    }

    int[] sizeRange(String id) {
        return "clock".equals(id) ? new int[]{40, MlKitException.CODE_SCANNER_UNAVAILABLE} : (id == null || !id.startsWith("st")) ? new int[]{40, MlKitException.LOW_LIGHT_AUTO_EXPOSURE_COMPUTATION_FAILURE} : new int[]{30, 400};
    }

    /* JADX WARN: Can't fix incorrect switch cases order, some code will duplicate */
    /* JADX WARN: Code restructure failed: missing block: B:7:0x0023, code lost:
    
        if (r6.equals("clock") != false) goto L17;
     */
    /*
        Code decompiled incorrectly, please refer to instructions dump.
    */
    void setSize(String id, int v) {
        if (id == null) {
        }
        int[] r = sizeRange(id);
        char c = 0;
        int v2 = Math.max(r[0], Math.min(r[1], v));
        switch (id.hashCode()) {
            case 97301:
                if (id.equals("bat")) {
                    c = 2;
                    break;
                }
                c = 65535;
                break;
            case 3076014:
                if (id.equals("date")) {
                    c = 1;
                    break;
                }
                c = 65535;
                break;
            case 94755854:
                break;
            default:
                c = 65535;
                break;
        }
        switch (c) {
            case 0:
                this.clockSize = v2;
                break;
            case 1:
                this.dateSize = v2;
                break;
            case 2:
                this.batSize = v2;
                break;
            default:
                Layer l = layer(id);
                if (l != null) {
                    l.size = v2;
                    break;
                } else {
                    Sticker s = sticker(id);
                    if (s != null) {
                        s.size = v2;
                        break;
                    }
                }
                break;
        }
    }

    FontSpec fontOf(String id) {
        if (id == null) {
            return null;
        }
        if (id.equals("clock")) {
            return this.font;
        }
        if (id.equals("date")) {
            return this.dateFont;
        }
        Layer l = layer(id);
        if (l == null) {
            return null;
        }
        return l.font;
    }

    Layer layer(String id) {
        if (id == null || !id.startsWith("tl")) {
            return null;
        }
        try {
            int i = Integer.parseInt(id.substring(2));
            if (i < 0 || i >= this.layers.size()) {
                return null;
            }
            return this.layers.get(i);
        } catch (NumberFormatException e) {
            return null;
        }
    }

    Sticker sticker(String id) {
        if (id == null || !id.startsWith("st")) {
            return null;
        }
        try {
            int i = Integer.parseInt(id.substring(2));
            if (i < 0 || i >= this.stickers.size()) {
                return null;
            }
            return this.stickers.get(i);
        } catch (NumberFormatException e) {
            return null;
        }
    }

    Layer webLayer() {
        for (Layer l : this.layers) {
            if (l.web && !l.url.trim().isEmpty()) {
                return l;
            }
        }
        return null;
    }

    boolean hasWebLayer() {
        for (Layer l : this.layers) {
            if (l.web) {
                return true;
            }
        }
        return false;
    }

    static Design load(Context c) {
        try {
            String s = Store.prefs(c).getString("design2", null);
            if (s != null) {
                Design d = fromJson(new JSONObject(s));
                d.attachRuntime(c);
                return d;
            }
        } catch (Exception e) {
        }
        Design d2 = new Design();
        d2.attachRuntime(c);
        return d2;
    }

    void save(Context c) {
        try {
            Store.prefs(c).edit().putString("design2", toJson().toString()).apply();
        } catch (Exception e) {
        }
    }

    Design copy() {
        try {
            return fromJson(toJson()).copyRuntime(this);
        } catch (Exception e) {
            return new Design().copyRuntime(this);
        }
    }

    JSONObject toJson() throws Exception {
        JSONObject o = new JSONObject();
        o.put("bgMode", this.bgMode).put("grad1", this.grad1).put("grad2", this.grad2).put("gradAnim", this.gradAnim);
        o.put("dim", this.dim).put("hue", this.hue).put("sat", this.sat);
        o.put("clockShow", this.clockShow).put("clockStyle", this.clockStyle).put("outline", this.outline).put("twoTone", this.twoTone);
        o.put("gradText", this.gradText).put("gradAngle", this.gradAngle);
        o.put("stroke", this.stroke).put("strokeW", this.strokeW).put("strokeColor", this.strokeColor).put("shadow", this.shadow);
        o.put("clockColor", this.clockColor).put("clockColor2", this.clockColor2).put("font", this.font.toJson());
        o.put("clockSize", this.clockSize).put("clockX", this.clockX).put("clockY", this.clockY).put("use24", this.use24);
        o.put("separator", this.separator).put("letterSpacing", this.letterSpacing).put("lineGap", this.lineGap);
        o.put("clockOver", this.clockOver).put("clockXf", this.clockXf.toJson());
        o.put("textColor", this.textColor);
        o.put("dateShow", this.dateShow).put("dateX", this.dateX).put("dateY", this.dateY).put("dateSize", this.dateSize);
        o.put("dateFmt", this.dateFmt).put("dateFont", this.dateFont.toJson()).put("dateXf", this.dateXf.toJson());
        o.put("batShow", this.batShow).put("batX", this.batX).put("batY", this.batY).put("batSize", this.batSize);
        o.put("batXf", this.batXf.toJson());
        o.put("parallax", this.parallax).put("parallaxStrength", this.parallaxStrength).put("parallaxText", this.parallaxText);
        o.put("particles", this.particles).put("particleCount", this.particleCount);
        o.put("pSpeed", this.pSpeed).put("pSize", this.pSize).put("pAlpha", this.pAlpha).put("pColor", this.pColor);
        o.put("autoColors", this.autoColors).put("weatherAuto", this.weatherAuto).put("weatherCity", this.weatherCity);
        if (!Double.isNaN(this.weatherLat) && !Double.isNaN(this.weatherLon)) {
            o.put("weatherLat", this.weatherLat).put("weatherLon", this.weatherLon);
        }
        JSONArray ls = new JSONArray();
        for (Layer l : this.layers) {
            ls.put(new JSONObject().put("web", l.web).put("text", l.text).put(ImagesContract.URL, l.url).put("refreshMin", l.refreshMin).put("x", l.x).put("y", l.y).put("size", l.size).put("color", l.color).put("font", l.font.toJson()).put("xf", l.xf.toJson()));
        }
        o.put("layers", ls);
        JSONArray a = new JSONArray();
        for (Sticker s : this.stickers) {
            a.put(new JSONObject().put("s", s.s).put("x", s.x).put("y", s.y).put("size", s.size).put("xf", s.xf.toJson()));
        }
        o.put("stickers", a);
        return o;
    }

    static Design fromJson(JSONObject o) {
        JSONArray a;
        int i;
        JSONArray ls;
        String str;
        String str2;
        String str3;
        boolean z;
        Design d = new Design();
        d.bgMode = o.optInt("bgMode", d.bgMode);
        d.grad1 = o.optInt("grad1", d.grad1);
        d.grad2 = o.optInt("grad2", d.grad2);
        d.gradAnim = o.optBoolean("gradAnim", d.gradAnim);
        d.dim = o.optInt("dim", d.dim);
        d.hue = o.optInt("hue", d.hue);
        d.sat = o.optInt("sat", d.sat);
        d.clockShow = o.optBoolean("clockShow", d.clockShow);
        d.clockStyle = o.optInt("clockStyle", d.clockStyle);
        d.outline = o.optBoolean("outline", d.outline);
        d.twoTone = o.optBoolean("twoTone", d.twoTone);
        d.gradText = o.optBoolean("gradText", d.gradText);
        d.gradAngle = o.optInt("gradAngle", d.gradAngle);
        d.stroke = o.optBoolean("stroke", d.stroke);
        d.strokeW = o.optInt("strokeW", d.strokeW);
        d.strokeColor = o.optInt("strokeColor", d.strokeColor);
        d.shadow = o.optBoolean("shadow", d.shadow);
        d.clockColor = o.optInt("clockColor", d.clockColor);
        d.clockColor2 = o.optInt("clockColor2", d.clockColor2);
        boolean z2 = false;
        if (!o.has("font")) {
            if (!o.has("fontId")) {
                if (o.has("clockFont")) {
                    d.font = new FontSpec("sys:" + o.optInt("clockFont", 0));
                }
            } else {
                d.font = new FontSpec(o.optString("fontId", "sys:0"));
            }
        } else {
            d.font = FontSpec.fromJson(o.optJSONObject("font"), d.font);
        }
        d.clockSize = o.optInt("clockSize", d.clockSize);
        d.clockX = (float) o.optDouble("clockX", d.clockX);
        d.clockY = (float) o.optDouble("clockY", d.clockY);
        d.use24 = o.optBoolean("use24", d.use24);
        d.separator = Math.max(0, Math.min(SEPARATORS.length - 1, o.optInt("separator", d.separator)));
        d.letterSpacing = o.optInt("letterSpacing", d.letterSpacing);
        d.lineGap = o.optInt("lineGap", d.lineGap);
        d.clockOver = o.optBoolean("clockOver", d.clockOver);
        d.clockXf = Xf.fromJson(o.optJSONObject("clockXf"));
        d.textColor = o.optInt("textColor", d.textColor);
        d.dateShow = o.optBoolean("dateShow", d.dateShow);
        d.dateX = (float) o.optDouble("dateX", d.dateX);
        d.dateY = (float) o.optDouble("dateY", d.dateY);
        d.dateSize = o.optInt("dateSize", d.dateSize);
        d.dateFmt = Math.max(0, Math.min(DATE_FORMATS.length - 1, o.optInt("dateFmt", d.dateFmt)));
        d.dateFont = FontSpec.fromJson(o.optJSONObject("dateFont"), new FontSpec(d.font.id.startsWith("sys:") ? d.font.id : "sys:0"));
        d.dateXf = Xf.fromJson(o.optJSONObject("dateXf"));
        d.batShow = o.optBoolean("batShow", d.batShow);
        d.batX = (float) o.optDouble("batX", d.batX);
        d.batY = (float) o.optDouble("batY", d.batY);
        d.batSize = o.optInt("batSize", d.batSize);
        d.batXf = Xf.fromJson(o.optJSONObject("batXf"));
        d.parallax = o.optBoolean("parallax", d.parallax);
        d.parallaxStrength = o.optInt("parallaxStrength", d.parallaxStrength);
        d.parallaxText = o.optBoolean("parallaxText", d.parallaxText);
        d.particles = o.optInt("particles", d.particles);
        d.particleCount = o.optInt("particleCount", d.particleCount);
        d.pSpeed = o.optInt("pSpeed", d.pSpeed);
        d.pSize = o.optInt("pSize", d.pSize);
        d.pAlpha = o.optInt("pAlpha", d.pAlpha);
        d.pColor = o.optInt("pColor", d.pColor);
        d.autoColors = o.optBoolean("autoColors", d.autoColors);
        d.weatherAuto = o.optBoolean("weatherAuto", d.weatherAuto);
        d.weatherCity = o.optString("weatherCity", d.weatherCity);
        if (o.has("weatherLat") && o.has("weatherLon")) {
            d.weatherLat = o.optDouble("weatherLat", Double.NaN);
            d.weatherLon = o.optDouble("weatherLon", Double.NaN);
        }
        JSONArray ls2 = o.optJSONArray("layers");
        String str4 = "refreshMin";
        String str5 = ImagesContract.URL;
        String str6 = "";
        if (ls2 == null) {
            if (!o.optString("text", "").isEmpty()) {
                Layer l = new Layer();
                l.text = o.optString("text", "");
                l.x = (float) o.optDouble("textX", 0.5d);
                l.y = (float) o.optDouble("textY", 0.62d);
                l.size = o.optInt("textSize", 100);
                d.layers.add(l);
            }
            if (!o.optString(ImagesContract.URL, "").isEmpty()) {
                Layer l2 = new Layer();
                l2.web = true;
                l2.url = o.optString(ImagesContract.URL, "");
                l2.refreshMin = Math.max(1, o.optInt("refreshMin", 30));
                l2.x = (float) o.optDouble("webX", 0.5d);
                l2.y = (float) o.optDouble("webY", 0.72d);
                l2.size = o.optInt("webSize", 100);
                d.layers.add(l2);
            }
        } else {
            int i2 = 0;
            while (i2 < ls2.length() && i2 < 20) {
                JSONObject j = ls2.optJSONObject(i2);
                if (j == null) {
                    ls = ls2;
                    str = str4;
                    str2 = str6;
                    str3 = str5;
                    z = z2;
                } else {
                    Layer l3 = new Layer();
                    l3.web = j.optBoolean("web", z2);
                    l3.text = j.optString("text", str6);
                    l3.url = j.optString(str5, str6);
                    ls = ls2;
                    l3.refreshMin = Math.max(1, j.optInt(str4, 30));
                    str = str4;
                    str2 = str6;
                    str3 = str5;
                    l3.x = (float) j.optDouble("x", 0.5d);
                    l3.y = (float) j.optDouble("y", 0.62d);
                    l3.size = j.optInt("size", 100);
                    z = false;
                    l3.color = j.optInt("color", 0);
                    l3.font = FontSpec.fromJson(j.optJSONObject("font"), new FontSpec());
                    l3.xf = Xf.fromJson(j.optJSONObject("xf"));
                    d.layers.add(l3);
                }
                i2++;
                str5 = str3;
                z2 = z;
                ls2 = ls;
                str4 = str;
                str6 = str2;
            }
        }
        JSONArray a2 = o.optJSONArray("stickers");
        if (a2 != null) {
            int i3 = 0;
            while (i3 < a2.length() && i3 < 30) {
                JSONObject s = a2.optJSONObject(i3);
                if (s == null) {
                    a = a2;
                    i = i3;
                } else {
                    a = a2;
                    i = i3;
                    Sticker st = new Sticker(s.optString("s", "⭐"), (float) s.optDouble("x", 0.5d), (float) s.optDouble("y", 0.5d));
                    st.size = s.optInt("size", 100);
                    st.xf = Xf.fromJson(s.optJSONObject("xf"));
                    d.stickers.add(st);
                }
                i3 = i + 1;
                a2 = a;
            }
        }
        return d;
    }
}
