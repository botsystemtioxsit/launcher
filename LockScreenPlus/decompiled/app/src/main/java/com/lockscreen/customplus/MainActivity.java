package com.lockscreen.customplus;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.ComponentName;
import android.content.DialogInterface;
import android.content.Intent;
import android.content.SharedPreferences;
import android.database.Cursor;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.text.Editable;
import android.text.TextUtils;
import android.view.View;
import android.view.ViewGroup;
import android.widget.EditText;
import android.widget.HorizontalScrollView;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import android.widget.Toast;
import androidx.core.view.ViewCompat;
import com.google.mlkit.common.MlKitException;
import com.lockscreen.customplus.Design;
import com.lockscreen.customplus.Fonts;
import com.lockscreen.customplus.PreviewView;
import com.lockscreen.customplus.Store;
import com.lockscreen.customplus.Ui;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;
import java.util.function.Consumer;
import java.util.function.IntConsumer;
import org.json.JSONArray;
import org.json.JSONObject;

/* loaded from: classes3.dex */
public class MainActivity extends Activity {
    private static final int REQ_BG = 1;
    private static final int REQ_CUT = 2;
    private static final int REQ_FONT = 3;
    private static final int SUB = -6512986;
    private static final int TAB_COLORS = 2;
    private static final int TAB_ELEMENTS = 3;
    private static final int TAB_MINE = 5;
    private static final int TEXT = -723722;
    private LinearLayout content;
    private Design design;
    private TextView fetchResult;
    private boolean fromEditor;
    private LinearLayout nav;
    private TextView photoStatus;
    private PreviewView preview;
    private ScrollView scroll;
    private SharedPreferences sp;
    private Ui ui;
    private TextView weatherResult;
    private static final String[] TAB_ICONS = {"🖼", "🕒", "🎨", "🧩", "✨", "💾"};
    private static final String[] TAB_NAMES = {"Фон", "Часы", "Цвета", "Элементы", "Эффекты", "Мои"};
    private static final String[] COLOR_TARGETS = {"Часы", "Минуты", "Надписи", "Градиент 1", "Градиент 2"};
    private static final int[] PALETTE = {-1, -5317, -16718337, -49023, -9834322, -28416, -1754827, ViewCompat.MEASURED_STATE_MASK};
    private static final int[] GRADIENT_COLORS = {-14997707, -11830879, -7459358, -44753, -15623794, -13045891, -244117, -12624133, -551138, ViewCompat.MEASURED_STATE_MASK};
    private static final String[] EMOJI = {"😀", "😎", "🔥", "❤️", "⭐", "🌙", "⚡", "🎵", "🚀", "🌸", "👑", "💎"};
    private static final String[] PARTICLE_NAMES = {"Нет", "Снег", "Дождь", "Звёзды", "Пузыри", "Сакура"};
    private static final Set<String> FLEX_FONTS = new HashSet(Arrays.asList("asset:RobotoFlex.ttf"));
    private static final Set<String> WEIGHT_FONTS = new HashSet(Arrays.asList("asset:Oswald.ttf", "asset:Orbitron.ttf", "asset:Comfortaa.ttf", "asset:PlayfairDisplay.ttf"));
    private int tab = 0;
    private String selectedId = "clock";
    private int colorTarget = 0;
    private final List<PreviewView> minis = new ArrayList();

    @Override // android.app.Activity
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        Fonts.init(this);
        this.sp = Store.prefs(this);
        this.ui = new Ui(this);
        this.design = Design.load(this);
        getWindow().setStatusBarColor(-16119284);
        getWindow().setNavigationBarColor(-16119284);
        LinearLayout linearLayout = new LinearLayout(this);
        linearLayout.setOrientation(1);
        linearLayout.setBackgroundColor(-16119284);
        LinearLayout head = new LinearLayout(this);
        head.setOrientation(0);
        head.setGravity(16);
        head.setPadding(this.ui.dp(20), this.ui.dp(10), this.ui.dp(16), this.ui.dp(4));
        TextView title = this.ui.label("Экран блокировки", 19.0f, TEXT, true);
        title.setSingleLine(true);
        title.setEllipsize(TextUtils.TruncateAt.END);
        head.addView(title, new LinearLayout.LayoutParams(0, -2, 1.0f));
        head.addView(this.ui.pill("⤢", false, new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda30
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.openEditor();
            }
        }));
        head.addView(this.ui.space(8));
        head.addView(this.ui.pill("Установить", true, new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda31
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.applyWallpaper();
            }
        }));
        linearLayout.addView(head);
        int screenH = getResources().getDisplayMetrics().heightPixels;
        int ph = Math.round(screenH * 0.28f);
        int pw = Math.round(ph / 2.1666667f);
        this.preview = new PreviewView(this);
        this.preview.interactive = true;
        this.preview.design = this.design;
        this.preview.selected = this.selectedId;
        this.preview.listener = new PreviewView.Listener() { // from class: com.lockscreen.customplus.MainActivity.1
            @Override // com.lockscreen.customplus.PreviewView.Listener
            public void onSelect(String id) {
                if (id != null && !id.equals(MainActivity.this.selectedId)) {
                    MainActivity.this.selectedId = id;
                    if (MainActivity.this.tab == 3) {
                        MainActivity.this.rebuild();
                    }
                }
            }

            @Override // com.lockscreen.customplus.PreviewView.Listener
            public void onMoved() {
                MainActivity.this.refreshMinis();
                if (MainActivity.this.tab == 3) {
                    MainActivity.this.rebuild();
                }
            }
        };
        LinearLayout.LayoutParams plp = new LinearLayout.LayoutParams(pw, ph);
        plp.gravity = 1;
        plp.setMargins(0, this.ui.dp(6), 0, this.ui.dp(4));
        linearLayout.addView(this.preview, plp);
        TextView hint = this.ui.label("Двигайте пальцем · щипок — размер · два пальца — поворот", 12.0f, SUB, false);
        hint.setGravity(17);
        linearLayout.addView(hint);
        this.scroll = new ScrollView(this);
        this.scroll.setVerticalScrollBarEnabled(false);
        this.content = new LinearLayout(this);
        this.content.setOrientation(1);
        this.content.setPadding(this.ui.dp(14), this.ui.dp(10), this.ui.dp(14), this.ui.dp(16));
        this.scroll.addView(this.content, new ViewGroup.LayoutParams(-1, -2));
        linearLayout.addView(this.scroll, new LinearLayout.LayoutParams(-1, 0, 1.0f));
        linearLayout.addView(buildNav());
        setContentView(linearLayout);
        showTab(0);
    }

    @Override // android.app.Activity
    protected void onResume() {
        super.onResume();
        if (this.preview == null) {
            return;
        }
        if (this.fromEditor) {
            this.fromEditor = false;
            this.design = Design.load(this);
            this.preview.design = this.design;
            rebuild();
        }
        this.preview.invalidate();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public void openEditor() {
        this.fromEditor = true;
        Intent i = new Intent(this, (Class<?>) EditorActivity.class);
        i.putExtra("selected", this.selectedId);
        startActivity(i);
    }

    private View buildNav() {
        this.nav = new LinearLayout(this);
        this.nav.setOrientation(0);
        this.nav.setBackgroundColor(-15658732);
        this.nav.setPadding(this.ui.dp(6), this.ui.dp(6), this.ui.dp(6), this.ui.dp(8));
        for (int i = 0; i < TAB_NAMES.length; i++) {
            final int idx = i;
            LinearLayout item = new LinearLayout(this);
            item.setOrientation(1);
            item.setGravity(1);
            TextView icon = this.ui.label(TAB_ICONS[i], 20.0f, TEXT, false);
            icon.setGravity(17);
            icon.setPadding(this.ui.dp(13), this.ui.dp(3), this.ui.dp(13), this.ui.dp(3));
            TextView name = this.ui.label(TAB_NAMES[i], 11.0f, SUB, false);
            name.setGravity(17);
            item.addView(icon);
            item.addView(name);
            item.setOnClickListener(new View.OnClickListener() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda94
                @Override // android.view.View.OnClickListener
                public final void onClick(View view) {
                    MainActivity.this.lambda$buildNav$0(idx, view);
                }
            });
            this.nav.addView(item, new LinearLayout.LayoutParams(0, -2, 1.0f));
        }
        return this.nav;
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildNav$0(int idx, View v) {
        showTab(idx);
    }

    private void updateNav() {
        for (int i = 0; i < this.nav.getChildCount(); i++) {
            LinearLayout item = (LinearLayout) this.nav.getChildAt(i);
            boolean on = false;
            TextView icon = (TextView) item.getChildAt(0);
            TextView name = (TextView) item.getChildAt(1);
            if (i == this.tab) {
                on = true;
            }
            icon.setBackground(on ? this.ui.rr(1430163967, 16) : null);
            name.setTextColor(on ? TEXT : SUB);
        }
    }

    private void showTab(int i) {
        this.tab = i;
        this.minis.clear();
        this.photoStatus = null;
        this.fetchResult = null;
        this.weatherResult = null;
        this.content.removeAllViews();
        updateNav();
        switch (i) {
            case 0:
                buildBackgroundTab();
                break;
            case 1:
                buildClockTab();
                break;
            case 2:
                buildColorsTab();
                break;
            case 3:
                buildElementsTab();
                break;
            case 4:
                buildEffectsTab();
                break;
            default:
                buildMineTab();
                break;
        }
        this.scroll.scrollTo(0, 0);
    }

    /* JADX INFO: Access modifiers changed from: private */
    public void rebuild() {
        final int y = this.scroll.getScrollY();
        showTab(this.tab);
        this.scroll.post(new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda71
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.lambda$rebuild$1(y);
            }
        });
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$rebuild$1(int y) {
        this.scroll.scrollTo(0, y);
    }

    /* JADX INFO: Access modifiers changed from: private */
    public void commit() {
        this.design.save(this);
        this.preview.invalidate();
        refreshMinis();
    }

    private void setDesign(Design nd) {
        nd.attachRuntime(this);
        this.design = nd;
        this.preview.design = nd;
        this.selectedId = "clock";
        this.preview.selected = this.selectedId;
        commit();
        showTab(this.tab);
    }

    private void select(String id) {
        this.selectedId = id;
        this.preview.selected = id;
        this.preview.invalidate();
    }

    private void buildBackgroundTab() {
        LinearLayout c = this.ui.card(this.content, "Фото");
        this.photoStatus = this.ui.label("", 14.0f, SUB, false);
        c.addView(this.photoStatus);
        LinearLayout row = this.ui.hrow();
        row.addView(this.ui.pill("Выбрать фото", true, new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda54
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.lambda$buildBackgroundTab$2();
            }
        }), this.ui.wrap());
        row.addView(this.ui.space(8));
        row.addView(this.ui.pill("Вырез (PNG)", false, new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda61
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.lambda$buildBackgroundTab$3();
            }
        }), this.ui.wrap());
        c.addView(row);
        c.addView(this.ui.label("Вырез — это объект с фото на прозрачном фоне. На Samsung: откройте фото в Галерее, долго нажмите на объект и выберите «Сохранить как изображение». Приложение само найдёт место выреза на фото.", 12.0f, SUB, false));
        LinearLayout nudge = this.ui.hrow();
        nudge.addView(this.ui.pill("←", false, new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda63
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.lambda$buildBackgroundTab$4();
            }
        }), this.ui.wrap());
        nudge.addView(this.ui.space(6));
        nudge.addView(this.ui.pill("→", false, new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda64
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.lambda$buildBackgroundTab$5();
            }
        }), this.ui.wrap());
        nudge.addView(this.ui.space(6));
        nudge.addView(this.ui.pill("↑", false, new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda65
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.lambda$buildBackgroundTab$6();
            }
        }), this.ui.wrap());
        nudge.addView(this.ui.space(6));
        nudge.addView(this.ui.pill("↓", false, new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda66
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.lambda$buildBackgroundTab$7();
            }
        }), this.ui.wrap());
        nudge.addView(this.ui.space(10));
        nudge.addView(this.ui.pill("Убрать вырез", false, new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda67
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.lambda$buildBackgroundTab$8();
            }
        }), this.ui.wrap());
        c.addView(nudge);
        TextView auto = this.ui.pill("Вырезать объект автоматически (бета)", false, new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda68
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.runAutoCut();
            }
        });
        LinearLayout.LayoutParams alp = this.ui.wrap();
        alp.topMargin = this.ui.dp(10);
        c.addView(auto, alp);
        c.addView(this.ui.label("Работает на телефоне через Google. Если не получится, покажу причину.", 12.0f, SUB, false));
        refreshPhotoStatus();
        LinearLayout b = this.ui.card(this.content, "Тип фона");
        b.addView(this.ui.chips(new String[]{"Фото", "Градиент"}, this.design.bgMode, new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda69
            @Override // java.util.function.IntConsumer
            public final void accept(int i) {
                MainActivity.this.lambda$buildBackgroundTab$9(i);
            }
        }));
        if (this.design.bgMode == 1) {
            b.addView(this.ui.label("Первый цвет", 13.0f, SUB, false));
            b.addView(this.ui.swatches(GRADIENT_COLORS, this.design.grad1, new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda70
                @Override // java.util.function.IntConsumer
                public final void accept(int i) {
                    MainActivity.this.lambda$buildBackgroundTab$10(i);
                }
            }));
            b.addView(this.ui.label("Второй цвет", 13.0f, SUB, false));
            b.addView(this.ui.swatches(GRADIENT_COLORS, this.design.grad2, new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda55
                @Override // java.util.function.IntConsumer
                public final void accept(int i) {
                    MainActivity.this.lambda$buildBackgroundTab$11(i);
                }
            }));
            b.addView(this.ui.sw("Плавное движение цвета", this.design.gradAnim, new Consumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda56
                @Override // java.util.function.Consumer
                public final void accept(Object obj) {
                    MainActivity.this.lambda$buildBackgroundTab$12((Boolean) obj);
                }
            }));
        }
        b.addView(this.ui.slider("Затемнение", 0, 70, this.design.dim, "%", new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda57
            @Override // java.util.function.IntConsumer
            public final void accept(int i) {
                MainActivity.this.lambda$buildBackgroundTab$13(i);
            }
        }));
        LinearLayout k = this.ui.card(this.content, "Цвет обоев");
        k.addView(this.ui.slider("Оттенок", -180, 180, this.design.hue, "°", new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda58
            @Override // java.util.function.IntConsumer
            public final void accept(int i) {
                MainActivity.this.lambda$buildBackgroundTab$14(i);
            }
        }));
        k.addView(this.ui.slider("Насыщенность", 0, MlKitException.CODE_SCANNER_UNAVAILABLE, this.design.sat, "%", new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda59
            @Override // java.util.function.IntConsumer
            public final void accept(int i) {
                MainActivity.this.lambda$buildBackgroundTab$15(i);
            }
        }));
        k.addView(this.ui.pill("Сбросить цвет обоев", false, new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda60
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.lambda$buildBackgroundTab$16();
            }
        }));
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildBackgroundTab$2() {
        pickImage(1);
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildBackgroundTab$3() {
        pickImage(2);
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildBackgroundTab$4() {
        nudgeCut(-6, 0);
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildBackgroundTab$5() {
        nudgeCut(6, 0);
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildBackgroundTab$6() {
        nudgeCut(0, -6);
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildBackgroundTab$7() {
        nudgeCut(0, 6);
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildBackgroundTab$8() {
        Store.removeCut(this);
        refreshAfterImages();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildBackgroundTab$9(int i) {
        this.design.bgMode = i;
        commit();
        rebuild();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildBackgroundTab$10(int col) {
        this.design.grad1 = col;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildBackgroundTab$11(int col) {
        this.design.grad2 = col;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildBackgroundTab$12(Boolean v) {
        this.design.gradAnim = v.booleanValue();
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildBackgroundTab$13(int v) {
        this.design.dim = v;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildBackgroundTab$14(int v) {
        this.design.hue = v;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildBackgroundTab$15(int v) {
        this.design.sat = v;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildBackgroundTab$16() {
        this.design.hue = 0;
        this.design.sat = 100;
        commit();
        rebuild();
    }

    private void refreshPhotoStatus() {
        if (this.photoStatus == null) {
            return;
        }
        this.photoStatus.setText("Фото: " + (Store.hasBackground(this) ? "есть" : "нет") + "     Вырез: " + (Store.hasCut(this) ? "есть" : "нет"));
    }

    private void nudgeCut(final int dx, final int dy) {
        new Thread(new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda83
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.lambda$nudgeCut$17(dx, dy);
            }
        }).start();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$nudgeCut$17(int dx, int dy) {
        try {
            Store.nudge(this, dx, dy);
        } catch (Exception e) {
        }
        runOnUiThread(new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda77
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.refreshAfterImages();
            }
        });
    }

    /* JADX INFO: Access modifiers changed from: private */
    public void refreshAfterImages() {
        refreshPhotoStatus();
        this.preview.invalidate();
        refreshMinis();
    }

    private void buildClockTab() {
        LinearLayout top = this.ui.card(this.content, null);
        top.addView(this.ui.sw("Показывать часы", this.design.clockShow, new Consumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda96
            @Override // java.util.function.Consumer
            public final void accept(Object obj) {
                MainActivity.this.lambda$buildClockTab$18((Boolean) obj);
            }
        }));
        this.content.addView(this.ui.label("Стили", 16.0f, TEXT, true), titleLp());
        for (int i = 0; i < Design.STYLE_NAMES.length; i += 2) {
            LinearLayout row = this.ui.hrow();
            for (int j = i; j < Math.min(i + 2, Design.STYLE_NAMES.length); j++) {
                row.addView(styleCard(j), new LinearLayout.LayoutParams(0, -2, 1.0f));
            }
            this.content.addView(row);
        }
        LinearLayout o = this.ui.card(this.content, "Цифры");
        o.addView(this.ui.slider("Размер", 40, MlKitException.CODE_SCANNER_UNAVAILABLE, this.design.clockSize, "", new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda102
            @Override // java.util.function.IntConsumer
            public final void accept(int i2) {
                MainActivity.this.lambda$buildClockTab$19(i2);
            }
        }));
        o.addView(this.ui.slider("Расстояние между цифрами", -10, 40, this.design.letterSpacing, "", new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda103
            @Override // java.util.function.IntConsumer
            public final void accept(int i2) {
                MainActivity.this.lambda$buildClockTab$20(i2);
            }
        }));
        o.addView(this.ui.slider("Расстояние между рядами (в два ряда)", 10, 80, this.design.lineGap, "", new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda104
            @Override // java.util.function.IntConsumer
            public final void accept(int i2) {
                MainActivity.this.lambda$buildClockTab$21(i2);
            }
        }));
        o.addView(this.ui.label("Разделитель (в одну строку)", 13.0f, SUB, false));
        o.addView(this.ui.chips(Design.SEPARATOR_NAMES, this.design.separator, new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda105
            @Override // java.util.function.IntConsumer
            public final void accept(int i2) {
                MainActivity.this.lambda$buildClockTab$22(i2);
            }
        }));
        o.addView(this.ui.sw("24-часовой формат", this.design.use24, new Consumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda1
            @Override // java.util.function.Consumer
            public final void accept(Object obj) {
                MainActivity.this.lambda$buildClockTab$23((Boolean) obj);
            }
        }));
        o.addView(this.ui.sw("Часы поверх выреза (а не за ним)", this.design.clockOver, new Consumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda2
            @Override // java.util.function.Consumer
            public final void accept(Object obj) {
                MainActivity.this.lambda$buildClockTab$24((Boolean) obj);
            }
        }));
        LinearLayout col = this.ui.card(this.content, "Цвет и вид");
        col.addView(this.ui.label("Цвет", 13.0f, SUB, false));
        col.addView(this.ui.swatches(PALETTE, this.design.clockColor, new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda3
            @Override // java.util.function.IntConsumer
            public final void accept(int i2) {
                MainActivity.this.lambda$buildClockTab$25(i2);
            }
        }));
        col.addView(this.ui.label("Второй цвет (минуты, градиент)", 13.0f, SUB, false));
        col.addView(this.ui.swatches(PALETTE, this.design.clockColor2, new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda4
            @Override // java.util.function.IntConsumer
            public final void accept(int i2) {
                MainActivity.this.lambda$buildClockTab$26(i2);
            }
        }));
        col.addView(this.ui.sw("Градиент на цифрах", this.design.gradText, new Consumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda5
            @Override // java.util.function.Consumer
            public final void accept(Object obj) {
                MainActivity.this.lambda$buildClockTab$27((Boolean) obj);
            }
        }));
        col.addView(this.ui.slider("Угол градиента", 0, 360, this.design.gradAngle, "°", new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda97
            @Override // java.util.function.IntConsumer
            public final void accept(int i2) {
                MainActivity.this.lambda$buildClockTab$28(i2);
            }
        }));
        col.addView(this.ui.sw("Тень", this.design.shadow, new Consumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda98
            @Override // java.util.function.Consumer
            public final void accept(Object obj) {
                MainActivity.this.lambda$buildClockTab$29((Boolean) obj);
            }
        }));
        col.addView(this.ui.sw("Обводка", this.design.stroke, new Consumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda99
            @Override // java.util.function.Consumer
            public final void accept(Object obj) {
                MainActivity.this.lambda$buildClockTab$30((Boolean) obj);
            }
        }));
        col.addView(this.ui.slider("Толщина обводки", 1, 30, this.design.strokeW, "", new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda100
            @Override // java.util.function.IntConsumer
            public final void accept(int i2) {
                MainActivity.this.lambda$buildClockTab$31(i2);
            }
        }));
        col.addView(this.ui.label("Цвет обводки", 13.0f, SUB, false));
        col.addView(this.ui.swatches(PALETTE, this.design.strokeColor, new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda101
            @Override // java.util.function.IntConsumer
            public final void accept(int i2) {
                MainActivity.this.lambda$buildClockTab$32(i2);
            }
        }));
        LinearLayout f = this.ui.card(this.content, "Шрифт и форма цифр");
        fontSection(f, this.design.font);
        f.addView(this.ui.label("Положение, наклон и поворот — на вкладке «Элементы» или щипком на предпросмотре.", 12.0f, SUB, false));
        refreshMinis();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildClockTab$18(Boolean v) {
        this.design.clockShow = v.booleanValue();
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildClockTab$19(int v) {
        this.design.clockSize = v;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildClockTab$20(int v) {
        this.design.letterSpacing = v;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildClockTab$21(int v) {
        this.design.lineGap = v;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildClockTab$22(int i) {
        this.design.separator = i;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildClockTab$23(Boolean v) {
        this.design.use24 = v.booleanValue();
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildClockTab$24(Boolean v) {
        this.design.clockOver = v.booleanValue();
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildClockTab$25(int c) {
        this.design.clockColor = c;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildClockTab$26(int c) {
        this.design.clockColor2 = c;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildClockTab$27(Boolean v) {
        this.design.gradText = v.booleanValue();
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildClockTab$28(int v) {
        this.design.gradAngle = v;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildClockTab$29(Boolean v) {
        this.design.shadow = v.booleanValue();
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildClockTab$30(Boolean v) {
        this.design.stroke = v.booleanValue();
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildClockTab$31(int v) {
        this.design.strokeW = v;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildClockTab$32(int c) {
        this.design.strokeColor = c;
        commit();
    }

    private LinearLayout.LayoutParams titleLp() {
        LinearLayout.LayoutParams lp = this.ui.match();
        lp.topMargin = this.ui.dp(14);
        lp.leftMargin = this.ui.dp(6);
        return lp;
    }

    private View styleCard(final int idx) {
        LinearLayout box = new LinearLayout(this);
        box.setOrientation(1);
        box.setPadding(this.ui.dp(5), this.ui.dp(5), this.ui.dp(5), this.ui.dp(8));
        PreviewView mini = new PreviewView(this);
        mini.cropTop = true;
        mini.design = styled(idx);
        mini.setTag(Integer.valueOf(idx));
        int w = ((getResources().getDisplayMetrics().widthPixels - (this.ui.dp(14) * 2)) - (this.ui.dp(5) * 4)) / 2;
        box.addView(mini, new LinearLayout.LayoutParams(w, Math.round(w * 1.05f)));
        TextView name = this.ui.label(Design.STYLE_NAMES[idx], 13.0f, TEXT, false);
        name.setGravity(17);
        box.addView(name);
        mini.setOnClickListener(new View.OnClickListener() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda82
            @Override // android.view.View.OnClickListener
            public final void onClick(View view) {
                MainActivity.this.lambda$styleCard$33(idx, view);
            }
        });
        this.minis.add(mini);
        mini.selectedCard = Design.matchesStyle(this.design, idx);
        return box;
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$styleCard$33(int idx, View v) {
        Design.applyStyle(this.design, idx);
        commit();
    }

    private Design styled(int idx) {
        Design t = this.design.copy();
        Design.applyStyle(t, idx);
        t.clockX = 0.5f;
        t.clockY = 0.3f;
        t.clockSize = Math.max(70, Math.min(120, t.clockSize));
        t.clockXf.reset();
        t.dateShow = false;
        t.batShow = false;
        t.layers.clear();
        t.stickers.clear();
        t.particles = 0;
        t.weatherAuto = false;
        return t;
    }

    /* JADX INFO: Access modifiers changed from: private */
    public void refreshMinis() {
        for (PreviewView m : this.minis) {
            Object tag = m.getTag();
            if (tag instanceof Integer) {
                m.design = styled(((Integer) tag).intValue());
                m.selectedCard = Design.matchesStyle(this.design, ((Integer) tag).intValue());
            }
            m.invalidate();
        }
    }

    private void fontSection(LinearLayout parent, final Design.FontSpec spec) {
        String note;
        parent.addView(this.ui.label("Шрифт", 13.0f, SUB, false));
        parent.addView(fontChips(spec));
        parent.addView(this.ui.label("Конструктор шрифта", 14.0f, TEXT, true));
        String id = spec.id;
        if (FLEX_FONTS.contains(id)) {
            note = "Это гибкий шрифт: работают все ползунки.";
        } else if (WEIGHT_FONTS.contains(id)) {
            note = "У этого шрифта меняется только толщина. Для полной свободы выберите Roboto Flex.";
        } else {
            note = id.startsWith("user:") ? "Работает, если ваш файл — гибкий шрифт (variable)." : "У этого шрифта форма не меняется. Выберите Roboto Flex — он гибкий.";
        }
        parent.addView(this.ui.label(note, 12.0f, SUB, false));
        if (!FLEX_FONTS.contains(id)) {
            parent.addView(this.ui.pill("Включить гибкий шрифт Roboto Flex", false, new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda0
                @Override // java.lang.Runnable
                public final void run() {
                    MainActivity.this.lambda$fontSection$34(spec);
                }
            }));
        }
        parent.addView(axis("Толщина", 100, 1000, 400, spec.wght, new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda29
            @Override // java.util.function.IntConsumer
            public final void accept(int i) {
                Design.FontSpec.this.wght = i;
            }
        }));
        parent.addView(axis("Ширина (сплюснуть — растянуть)", 25, 151, 100, spec.wdth, new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda40
            @Override // java.util.function.IntConsumer
            public final void accept(int i) {
                Design.FontSpec.this.wdth = i;
            }
        }));
        parent.addView(axis("Наклон", -10, 0, 0, spec.slnt, new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda51
            @Override // java.util.function.IntConsumer
            public final void accept(int i) {
                Design.FontSpec.this.slnt = i;
            }
        }));
        parent.addView(axis("Высота цифр", 560, 788, 738, spec.hgt, new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda62
            @Override // java.util.function.IntConsumer
            public final void accept(int i) {
                Design.FontSpec.this.hgt = i;
            }
        }));
        parent.addView(axis("Толщина вертикальных штрихов", 27, 175, 96, spec.stem, new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda73
            @Override // java.util.function.IntConsumer
            public final void accept(int i) {
                Design.FontSpec.this.stem = i;
            }
        }));
        parent.addView(axis("Толщина горизонтальных штрихов", 25, 135, 79, spec.hstem, new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda84
            @Override // java.util.function.IntConsumer
            public final void accept(int i) {
                Design.FontSpec.this.hstem = i;
            }
        }));
        parent.addView(axis("Ширина внутри цифры", 323, 603, 468, spec.cnt, new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda95
            @Override // java.util.function.IntConsumer
            public final void accept(int i) {
                Design.FontSpec.this.cnt = i;
            }
        }));
        LinearLayout r = this.ui.hrow();
        r.addView(this.ui.pill("Сбросить форму", false, new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda106
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.lambda$fontSection$42(spec);
            }
        }), this.ui.wrap());
        r.addView(this.ui.space(8));
        r.addView(this.ui.pill("Свой шрифт…", false, new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda8
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.lambda$fontSection$43();
            }
        }), this.ui.wrap());
        r.addView(this.ui.space(8));
        r.addView(this.ui.pill("Удалить свой", false, new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda18
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.lambda$fontSection$44(spec);
            }
        }), this.ui.wrap());
        parent.addView(r);
        parent.addView(this.ui.label("Свой шрифт: файл TTF, OTF или TTC. Другие форматы (например WOFF) Android не читает.", 12.0f, SUB, false));
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$fontSection$34(Design.FontSpec spec) {
        spec.id = "asset:RobotoFlex.ttf";
        commit();
        rebuild();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$fontSection$42(Design.FontSpec spec) {
        spec.resetAxes();
        commit();
        rebuild();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$fontSection$43() {
        pickFile(3);
    }

    private View axis(String name, int min, int max, int def, int cur, final IntConsumer set) {
        return this.ui.slider(name, min, max, cur == -9999 ? def : cur, "", new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda80
            @Override // java.util.function.IntConsumer
            public final void accept(int i) {
                MainActivity.this.lambda$axis$45(set, i);
            }
        });
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$axis$45(IntConsumer set, int v) {
        set.accept(v);
        commit();
    }

    private View fontChips(final Design.FontSpec spec) {
        HorizontalScrollView horizontalScrollView = new HorizontalScrollView(this);
        boolean z = false;
        horizontalScrollView.setHorizontalScrollBarEnabled(false);
        LinearLayout row = this.ui.hrow();
        List<Fonts.Entry> fonts = Fonts.list();
        final List<TextView> items = new ArrayList<>();
        int selected = 0;
        int i = 0;
        while (i < fonts.size()) {
            final Fonts.Entry e = fonts.get(i);
            if (e.id.equals(spec.id)) {
                selected = i;
            }
            int selected2 = selected;
            final int idx = i;
            TextView t = this.ui.label(e.name, 16.0f, TEXT, z);
            t.setTypeface(Fonts.get(e.id));
            t.setPadding(this.ui.dp(16), this.ui.dp(9), this.ui.dp(16), this.ui.dp(9));
            items.add(t);
            t.setOnClickListener(new View.OnClickListener() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda34
                @Override // android.view.View.OnClickListener
                public final void onClick(View view) {
                    MainActivity.this.lambda$fontChips$46(items, idx, spec, e, view);
                }
            });
            LinearLayout.LayoutParams lp = this.ui.wrap();
            lp.rightMargin = this.ui.dp(6);
            lp.topMargin = this.ui.dp(4);
            lp.bottomMargin = this.ui.dp(4);
            row.addView(t, lp);
            i++;
            selected = selected2;
            fonts = fonts;
            z = false;
        }
        this.ui.styleChips(items, selected);
        horizontalScrollView.addView(row);
        return horizontalScrollView;
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$fontChips$46(List items, int idx, Design.FontSpec spec, Fonts.Entry e, View v) {
        this.ui.styleChips(items, idx);
        spec.id = e.id;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    /* renamed from: deleteUserFont, reason: merged with bridge method [inline-methods] */
    public void lambda$fontSection$44(Design.FontSpec spec) {
        if (!spec.id.startsWith("user:")) {
            toast("Выберите один из своих шрифтов");
            return;
        }
        String gone = spec.id;
        Fonts.deleteUser(this, gone);
        fixFontsAfterDelete(gone);
        commit();
        rebuild();
    }

    private void fixFontsAfterDelete(String gone) {
        if (this.design.font.id.equals(gone)) {
            this.design.font.id = "sys:0";
        }
        if (this.design.dateFont.id.equals(gone)) {
            this.design.dateFont.id = "sys:0";
        }
        for (Design.Layer l : this.design.layers) {
            if (l.font.id.equals(gone)) {
                l.font.id = "sys:0";
            }
        }
    }

    private void buildColorsTab() {
        LinearLayout c = this.ui.card(this.content, "Цвета системы");
        if (!SystemColors.available()) {
            c.addView(this.ui.label("Палитра системы есть на Android 12 и новее. На этом телефоне её нет.", 14.0f, SUB, false));
            return;
        }
        c.addView(this.ui.sw("Подбирать цвета автоматически", this.design.autoColors, new Consumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda90
            @Override // java.util.function.Consumer
            public final void accept(Object obj) {
                MainActivity.this.lambda$buildColorsTab$47((Boolean) obj);
            }
        }));
        c.addView(this.ui.label("Часы, надписи и градиент возьмут цвета из палитры вашего телефона (Настройки → Обои и стиль → Цветовая палитра) и поменяются вместе с ней.", 12.0f, SUB, false));
        LinearLayout m = this.ui.card(this.content, "Выбрать вручную");
        m.addView(this.ui.label("Куда применить цвет:", 13.0f, SUB, false));
        m.addView(this.ui.chips(COLOR_TARGETS, this.colorTarget, new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda91
            @Override // java.util.function.IntConsumer
            public final void accept(int i) {
                MainActivity.this.lambda$buildColorsTab$48(i);
            }
        }));
        for (int g = 0; g < SystemColors.GROUPS.length; g++) {
            m.addView(this.ui.label(SystemColors.GROUPS[g], 13.0f, SUB, false));
            int[] cols = new int[SystemColors.TONES.length];
            for (int t = 0; t < cols.length; t++) {
                cols[t] = SystemColors.get(this, g, SystemColors.TONES[t]);
            }
            m.addView(this.ui.swatches(cols, 1, new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda92
                @Override // java.util.function.IntConsumer
                public final void accept(int i) {
                    MainActivity.this.applySystemColor(i);
                }
            }));
        }
        m.addView(this.ui.label("Выбранный вручную цвет отключает автоматический подбор.", 12.0f, SUB, false));
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildColorsTab$47(Boolean v) {
        this.design.autoColors = v.booleanValue();
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildColorsTab$48(int i) {
        this.colorTarget = i;
    }

    /* JADX INFO: Access modifiers changed from: private */
    public void applySystemColor(int col) {
        switch (this.colorTarget) {
            case 0:
                this.design.clockColor = col;
                break;
            case 1:
                this.design.clockColor2 = col;
                this.design.twoTone = true;
                break;
            case 2:
                this.design.textColor = col;
                break;
            case 3:
                this.design.bgMode = 1;
                this.design.grad1 = col;
                break;
            default:
                this.design.bgMode = 1;
                this.design.grad2 = col;
                break;
        }
        boolean wasAuto = this.design.autoColors;
        this.design.autoColors = false;
        commit();
        if (wasAuto) {
            rebuild();
        }
    }

    private void buildElementsTab() {
        if (this.design.pos(this.selectedId) == null) {
            this.selectedId = "clock";
        }
        this.preview.selected = this.selectedId;
        final String id = this.selectedId;
        LinearLayout sel = this.ui.card(this.content, "Выбранный элемент");
        sel.addView(this.ui.label("Нажмите на элемент в предпросмотре или выберите здесь.", 12.0f, SUB, false));
        final List<String[]> els = this.design.elements();
        String[] names = new String[els.size()];
        int idx = 0;
        for (int i = 0; i < els.size(); i++) {
            names[i] = els.get(i)[1];
            if (els.get(i)[0].equals(id)) {
                idx = i;
            }
        }
        sel.addView(this.ui.chips(names, idx, new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda10
            @Override // java.util.function.IntConsumer
            public final void accept(int i2) {
                MainActivity.this.lambda$buildElementsTab$49(els, i2);
            }
        }));
        final Design.Xf xf = this.design.xf(id);
        int[] r = this.design.sizeRange(id);
        sel.addView(this.ui.slider("Размер", r[0], r[1], this.design.size(id), "", new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda20
            @Override // java.util.function.IntConsumer
            public final void accept(int i2) {
                MainActivity.this.lambda$buildElementsTab$50(id, i2);
            }
        }));
        sel.addView(this.ui.slider("Поворот", -180, 180, xf.rot, "°", new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda21
            @Override // java.util.function.IntConsumer
            public final void accept(int i2) {
                MainActivity.this.lambda$buildElementsTab$51(xf, i2);
            }
        }));
        sel.addView(this.ui.slider("Наклон вперёд-назад", -80, 80, xf.rx, "°", new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda22
            @Override // java.util.function.IntConsumer
            public final void accept(int i2) {
                MainActivity.this.lambda$buildElementsTab$52(xf, i2);
            }
        }));
        sel.addView(this.ui.slider("Наклон влево-вправо", -80, 80, xf.ry, "°", new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda23
            @Override // java.util.function.IntConsumer
            public final void accept(int i2) {
                MainActivity.this.lambda$buildElementsTab$53(xf, i2);
            }
        }));
        sel.addView(this.ui.slider("Перекос", -60, 60, xf.skew, "", new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda24
            @Override // java.util.function.IntConsumer
            public final void accept(int i2) {
                MainActivity.this.lambda$buildElementsTab$54(xf, i2);
            }
        }));
        sel.addView(this.ui.slider("Растянуть по ширине", 30, MlKitException.LOW_LIGHT_AUTO_EXPOSURE_COMPUTATION_FAILURE, xf.sx, "%", new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda25
            @Override // java.util.function.IntConsumer
            public final void accept(int i2) {
                MainActivity.this.lambda$buildElementsTab$55(xf, i2);
            }
        }));
        sel.addView(this.ui.slider("Растянуть по высоте", 30, MlKitException.LOW_LIGHT_AUTO_EXPOSURE_COMPUTATION_FAILURE, xf.sy, "%", new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda26
            @Override // java.util.function.IntConsumer
            public final void accept(int i2) {
                MainActivity.this.lambda$buildElementsTab$56(xf, i2);
            }
        }));
        sel.addView(this.ui.pill("Сбросить поворот и наклон", false, new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda27
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.lambda$buildElementsTab$57(xf);
            }
        }));
        if (id.equals("clock")) {
            LinearLayout c = this.ui.card(this.content, "Часы");
            c.addView(this.ui.sw("Показывать часы", this.design.clockShow, new Consumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda28
                @Override // java.util.function.Consumer
                public final void accept(Object obj) {
                    MainActivity.this.lambda$buildElementsTab$58((Boolean) obj);
                }
            }));
            c.addView(this.ui.label("Шрифт, цвет и стиль — на вкладке «Часы».", 12.0f, SUB, false));
        } else if (id.equals("date")) {
            LinearLayout c2 = this.ui.card(this.content, "Дата");
            c2.addView(this.ui.sw("Показывать дату", this.design.dateShow, new Consumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda11
                @Override // java.util.function.Consumer
                public final void accept(Object obj) {
                    MainActivity.this.lambda$buildElementsTab$59((Boolean) obj);
                }
            }));
            c2.addView(this.ui.label("Формат", 13.0f, SUB, false));
            c2.addView(this.ui.chips(Design.DATE_NAMES, this.design.dateFmt, new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda12
                @Override // java.util.function.IntConsumer
                public final void accept(int i2) {
                    MainActivity.this.lambda$buildElementsTab$60(i2);
                }
            }));
            fontSection(c2, this.design.dateFont);
        } else if (id.equals("bat")) {
            LinearLayout c3 = this.ui.card(this.content, "Батарея");
            c3.addView(this.ui.sw("Показывать заряд", this.design.batShow, new Consumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda13
                @Override // java.util.function.Consumer
                public final void accept(Object obj) {
                    MainActivity.this.lambda$buildElementsTab$61((Boolean) obj);
                }
            }));
            c3.addView(this.ui.label("Шрифт у батареи такой же, как у даты.", 12.0f, SUB, false));
        } else if (this.design.layer(id) != null) {
            layerCard(this.design.layer(id), id);
        } else if (this.design.sticker(id) != null) {
            LinearLayout c4 = this.ui.card(this.content, "Стикер");
            LinearLayout row = this.ui.hrow();
            row.addView(this.ui.pill("Копировать", false, new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda14
                @Override // java.lang.Runnable
                public final void run() {
                    MainActivity.this.lambda$buildElementsTab$62(id);
                }
            }), this.ui.wrap());
            row.addView(this.ui.space(8));
            row.addView(this.ui.pill("Удалить", false, new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda15
                @Override // java.lang.Runnable
                public final void run() {
                    MainActivity.this.lambda$buildElementsTab$63(id);
                }
            }), this.ui.wrap());
            c4.addView(row);
        }
        LinearLayout add = this.ui.card(this.content, "Добавить на экран");
        LinearLayout row2 = this.ui.hrow();
        row2.addView(this.ui.pill("+ Текст", true, new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda16
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.lambda$buildElementsTab$64();
            }
        }), this.ui.wrap());
        if (!this.design.hasWebLayer()) {
            row2.addView(this.ui.space(8));
            row2.addView(this.ui.pill("+ Текст из интернета", false, new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda17
                @Override // java.lang.Runnable
                public final void run() {
                    MainActivity.this.lambda$buildElementsTab$65();
                }
            }), this.ui.wrap());
        }
        add.addView(row2);
        add.addView(this.ui.label("Стикеры:", 13.0f, SUB, false));
        add.addView(emojiChips());
        LinearLayout col = this.ui.card(this.content, "Общий цвет надписей");
        col.addView(this.ui.swatches(PALETTE, this.design.textColor, new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda19
            @Override // java.util.function.IntConsumer
            public final void accept(int i2) {
                MainActivity.this.lambda$buildElementsTab$66(i2);
            }
        }));
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildElementsTab$49(List els, int i) {
        select(((String[]) els.get(i))[0]);
        rebuild();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildElementsTab$50(String id, int v) {
        this.design.setSize(id, v);
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildElementsTab$51(Design.Xf xf, int v) {
        xf.rot = v;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildElementsTab$52(Design.Xf xf, int v) {
        xf.rx = v;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildElementsTab$53(Design.Xf xf, int v) {
        xf.ry = v;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildElementsTab$54(Design.Xf xf, int v) {
        xf.skew = v;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildElementsTab$55(Design.Xf xf, int v) {
        xf.sx = v;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildElementsTab$56(Design.Xf xf, int v) {
        xf.sy = v;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildElementsTab$57(Design.Xf xf) {
        xf.reset();
        commit();
        rebuild();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildElementsTab$58(Boolean v) {
        this.design.clockShow = v.booleanValue();
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildElementsTab$59(Boolean v) {
        this.design.dateShow = v.booleanValue();
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildElementsTab$60(int i) {
        this.design.dateFmt = i;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildElementsTab$61(Boolean v) {
        this.design.batShow = v.booleanValue();
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildElementsTab$62(String id) {
        Design.Sticker s = this.design.sticker(id);
        if (s == null) {
            return;
        }
        Design.Sticker n = new Design.Sticker(s.s, Math.min(0.96f, s.x + 0.06f), Math.min(0.97f, s.y + 0.04f));
        n.size = s.size;
        n.xf = s.xf.copy();
        this.design.stickers.add(n);
        select("st" + (this.design.stickers.size() - 1));
        commit();
        rebuild();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildElementsTab$63(String id) {
        this.design.stickers.remove(this.design.sticker(id));
        select("clock");
        commit();
        rebuild();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildElementsTab$64() {
        addLayer(false);
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildElementsTab$65() {
        addLayer(true);
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildElementsTab$66(int c) {
        this.design.textColor = c;
        commit();
    }

    private void addLayer(boolean web) {
        if (this.design.layers.size() >= 20) {
            toast("Слишком много надписей");
            return;
        }
        Design.Layer l = new Design.Layer();
        l.web = web;
        l.y = Math.min(0.9f, (this.design.layers.size() * 0.06f) + 0.62f);
        if (!web) {
            l.text = "Новый текст";
        }
        this.design.layers.add(l);
        select("tl" + (this.design.layers.size() - 1));
        commit();
        rebuild();
    }

    private void layerCard(final Design.Layer l, String id) {
        LinearLayout c = this.ui.card(this.content, l.web ? "Текст из интернета" : "Текст");
        if (!l.web) {
            EditText et = this.ui.edit("Любой текст, можно в несколько строк", true);
            et.setText(l.text);
            et.addTextChangedListener(new Ui.SimpleWatcher() { // from class: com.lockscreen.customplus.MainActivity.2
                @Override // android.text.TextWatcher
                public void afterTextChanged(Editable s) {
                    l.text = s.toString();
                    MainActivity.this.commit();
                }
            });
            c.addView(et);
        } else {
            EditText url = this.ui.edit("Ссылка https://… (страница или обычный текст)", false);
            url.setText(l.url);
            url.addTextChangedListener(new Ui.SimpleWatcher() { // from class: com.lockscreen.customplus.MainActivity.3
                @Override // android.text.TextWatcher
                public void afterTextChanged(Editable s) {
                    l.url = s.toString().trim();
                    MainActivity.this.sp.edit().putString("fetchedText", "").putLong("fetchedAt", 0L).apply();
                    MainActivity.this.commit();
                }
            });
            c.addView(url);
            EditText every = this.ui.edit("Обновлять раз в … минут", false);
            every.setInputType(2);
            every.setText(String.valueOf(l.refreshMin));
            every.addTextChangedListener(new Ui.SimpleWatcher() { // from class: com.lockscreen.customplus.MainActivity.4
                @Override // android.text.TextWatcher
                public void afterTextChanged(Editable s) {
                    try {
                        l.refreshMin = Math.max(1, Integer.parseInt(s.toString().trim()));
                        MainActivity.this.commit();
                    } catch (NumberFormatException e) {
                    }
                }
            });
            c.addView(every);
            c.addView(this.ui.pill("Проверить ссылку", false, new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda85
                @Override // java.lang.Runnable
                public final void run() {
                    MainActivity.this.testFetch();
                }
            }));
            this.fetchResult = this.ui.label("", 13.0f, SUB, false);
            c.addView(this.fetchResult);
        }
        c.addView(this.ui.label("Цвет", 13.0f, SUB, false));
        c.addView(this.ui.swatches(PALETTE, l.color, new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda86
            @Override // java.util.function.IntConsumer
            public final void accept(int i) {
                MainActivity.this.lambda$layerCard$67(l, i);
            }
        }));
        c.addView(this.ui.pill("Общий цвет", false, new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda87
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.lambda$layerCard$68(l);
            }
        }));
        fontSection(c, l.font);
        LinearLayout row = this.ui.hrow();
        row.addView(this.ui.pill("Копировать", false, new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda88
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.lambda$layerCard$69(l);
            }
        }), this.ui.wrap());
        row.addView(this.ui.space(8));
        row.addView(this.ui.pill("Удалить", false, new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda89
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.lambda$layerCard$70(l);
            }
        }), this.ui.wrap());
        c.addView(row);
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$layerCard$67(Design.Layer l, int col) {
        l.color = col;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$layerCard$68(Design.Layer l) {
        l.color = 0;
        commit();
        rebuild();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$layerCard$69(Design.Layer l) {
        if (l.web) {
            toast("Надпись из интернета может быть одна");
            return;
        }
        this.design.layers.add(l.copy());
        select("tl" + (this.design.layers.size() - 1));
        commit();
        rebuild();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$layerCard$70(Design.Layer l) {
        this.design.layers.remove(l);
        select("clock");
        commit();
        rebuild();
    }

    private View emojiChips() {
        HorizontalScrollView horizontalScrollView = new HorizontalScrollView(this);
        horizontalScrollView.setHorizontalScrollBarEnabled(false);
        LinearLayout row = this.ui.hrow();
        for (final String e : EMOJI) {
            TextView t = this.ui.label(e, 24.0f, TEXT, false);
            t.setPadding(this.ui.dp(12), this.ui.dp(8), this.ui.dp(12), this.ui.dp(8));
            t.setBackground(this.ui.rr(-14013647, 20));
            t.setOnClickListener(new View.OnClickListener() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda79
                @Override // android.view.View.OnClickListener
                public final void onClick(View view) {
                    MainActivity.this.lambda$emojiChips$71(e, view);
                }
            });
            LinearLayout.LayoutParams lp = this.ui.wrap();
            lp.rightMargin = this.ui.dp(6);
            row.addView(t, lp);
        }
        horizontalScrollView.addView(row);
        return horizontalScrollView;
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$emojiChips$71(String e, View v) {
        if (this.design.stickers.size() >= 30) {
            toast("Слишком много стикеров");
            return;
        }
        this.design.stickers.add(new Design.Sticker(e, 0.5f, 0.85f));
        select("st" + (this.design.stickers.size() - 1));
        commit();
        rebuild();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public void testFetch() {
        Design.Layer web = this.design.webLayer();
        if (web == null) {
            if (this.fetchResult != null) {
                this.fetchResult.setText("Сначала вставьте ссылку.");
            }
        } else {
            final String url = web.url.trim();
            if (this.fetchResult != null) {
                this.fetchResult.setText("Загружаю…");
            }
            new Thread(new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda93
                @Override // java.lang.Runnable
                public final void run() {
                    MainActivity.this.lambda$testFetch$73(url);
                }
            }).start();
        }
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$testFetch$73(String url) {
        String t;
        try {
            String t2 = Store.fetchText(url);
            this.sp.edit().putString("fetchedText", t2).putLong("fetchedAt", System.currentTimeMillis()).apply();
            t = "Получилось:\n" + t2;
        } catch (Exception e) {
            t = "Не получилось: " + e.getMessage();
        }
        final String r = t;
        runOnUiThread(new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda81
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.lambda$testFetch$72(r);
            }
        });
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$testFetch$72(String r) {
        if (this.fetchResult != null) {
            this.fetchResult.setText(r);
        }
        this.preview.invalidate();
    }

    private void buildEffectsTab() {
        LinearLayout p = this.ui.card(this.content, "Сдвиг при наклоне");
        p.addView(this.ui.sw("Обои и вырез двигаются при наклоне телефона", this.design.parallax, new Consumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda35
            @Override // java.util.function.Consumer
            public final void accept(Object obj) {
                MainActivity.this.lambda$buildEffectsTab$74((Boolean) obj);
            }
        }));
        p.addView(this.ui.slider("Сила", 0, 100, this.design.parallaxStrength, "%", new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda39
            @Override // java.util.function.IntConsumer
            public final void accept(int i) {
                MainActivity.this.lambda$buildEffectsTab$75(i);
            }
        }));
        p.addView(this.ui.sw("Часы и надписи тоже двигаются", this.design.parallaxText, new Consumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda41
            @Override // java.util.function.Consumer
            public final void accept(Object obj) {
                MainActivity.this.lambda$buildEffectsTab$76((Boolean) obj);
            }
        }));
        p.addView(this.ui.label("Лучше всего с вырезом: часы окажутся между фоном и объектом.", 12.0f, SUB, false));
        LinearLayout w = this.ui.card(this.content, "Погода за окном");
        w.addView(this.ui.sw("Эффекты по реальной погоде", this.design.weatherAuto, new Consumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda42
            @Override // java.util.function.Consumer
            public final void accept(Object obj) {
                MainActivity.this.lambda$buildEffectsTab$77((Boolean) obj);
            }
        }));
        w.addView(this.ui.label("Идёт дождь — на экране дождь, снег — снег, гроза — дождь со вспышками. Ясно или пасмурно — экран чистый. Обновляется раз в 30 минут.", 12.0f, SUB, false));
        final EditText city = this.ui.edit("Ваш город, например Москва", false);
        city.setInputType(1);
        city.setText(this.design.weatherCity);
        w.addView(city);
        w.addView(this.ui.pill("Найти город и погоду", true, new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda43
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.lambda$buildEffectsTab$78(city);
            }
        }));
        this.weatherResult = this.ui.label(weatherSummary(), 13.0f, SUB, false);
        w.addView(this.weatherResult);
        w.addView(this.ui.label("Пробный показ (только здесь, на экран блокировки не влияет):", 12.0f, SUB, false));
        final int[] codes = {-1, 0, 63, 73, 95};
        w.addView(this.ui.chips(new String[]{"Реальная", "Ясно", "Дождь", "Снег", "Гроза"}, 0, new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda44
            @Override // java.util.function.IntConsumer
            public final void accept(int i) {
                MainActivity.this.lambda$buildEffectsTab$79(codes, i);
            }
        }));
        w.addView(this.ui.label("Данные о погоде: Open-Meteo (без регистрации). Нужен интернет.", 12.0f, SUB, false));
        LinearLayout f = this.ui.card(this.content, "Частицы вручную");
        f.addView(this.ui.label("Работают, когда погодные эффекты выключены.", 12.0f, SUB, false));
        f.addView(this.ui.chips(PARTICLE_NAMES, this.design.particles, new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda45
            @Override // java.util.function.IntConsumer
            public final void accept(int i) {
                MainActivity.this.lambda$buildEffectsTab$80(i);
            }
        }));
        f.addView(this.ui.slider("Количество", 10, MlKitException.CODE_SCANNER_UNAVAILABLE, this.design.particleCount, "", new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda46
            @Override // java.util.function.IntConsumer
            public final void accept(int i) {
                MainActivity.this.lambda$buildEffectsTab$81(i);
            }
        }));
        f.addView(this.ui.slider("Скорость", 20, MlKitException.LOW_LIGHT_AUTO_EXPOSURE_COMPUTATION_FAILURE, this.design.pSpeed, "%", new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda47
            @Override // java.util.function.IntConsumer
            public final void accept(int i) {
                MainActivity.this.lambda$buildEffectsTab$82(i);
            }
        }));
        f.addView(this.ui.slider("Размер", 30, MlKitException.LOW_LIGHT_AUTO_EXPOSURE_COMPUTATION_FAILURE, this.design.pSize, "%", new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda48
            @Override // java.util.function.IntConsumer
            public final void accept(int i) {
                MainActivity.this.lambda$buildEffectsTab$83(i);
            }
        }));
        f.addView(this.ui.slider("Яркость", 10, 100, this.design.pAlpha, "%", new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda36
            @Override // java.util.function.IntConsumer
            public final void accept(int i) {
                MainActivity.this.lambda$buildEffectsTab$84(i);
            }
        }));
        f.addView(this.ui.label("Цвет частиц", 13.0f, SUB, false));
        f.addView(this.ui.swatches(PALETTE, this.design.pColor, new IntConsumer() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda37
            @Override // java.util.function.IntConsumer
            public final void accept(int i) {
                MainActivity.this.lambda$buildEffectsTab$85(i);
            }
        }));
        f.addView(this.ui.pill("Цвет по умолчанию", false, new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda38
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.lambda$buildEffectsTab$86();
            }
        }));
        f.addView(this.ui.label("Частицы и движение расходуют немного больше батареи.", 12.0f, SUB, false));
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildEffectsTab$74(Boolean v) {
        this.design.parallax = v.booleanValue();
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildEffectsTab$75(int v) {
        this.design.parallaxStrength = v;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildEffectsTab$76(Boolean v) {
        this.design.parallaxText = v.booleanValue();
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildEffectsTab$77(Boolean v) {
        this.design.weatherAuto = v.booleanValue();
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildEffectsTab$78(EditText city) {
        findWeather(city.getText().toString());
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildEffectsTab$79(int[] codes, int i) {
        if (i == 0) {
            this.design.attachRuntime(this);
        } else {
            this.design.weatherCode = codes[i];
        }
        if (!this.design.weatherAuto) {
            toast("Включите «Эффекты по реальной погоде»");
        }
        this.preview.invalidate();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildEffectsTab$80(int i) {
        this.design.particles = i;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildEffectsTab$81(int v) {
        this.design.particleCount = v;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildEffectsTab$82(int v) {
        this.design.pSpeed = v;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildEffectsTab$83(int v) {
        this.design.pSize = v;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildEffectsTab$84(int v) {
        this.design.pAlpha = v;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildEffectsTab$85(int c) {
        this.design.pColor = c;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildEffectsTab$86() {
        this.design.pColor = 0;
        commit();
        rebuild();
    }

    private String weatherSummary() {
        long age = System.currentTimeMillis() - this.sp.getLong("weatherAt", 0L);
        if (this.design.weatherCity.isEmpty()) {
            return "Город не выбран.";
        }
        if (age > 14400000 || !this.sp.contains("weatherCode")) {
            return this.design.weatherCity + ": погода ещё не получена.";
        }
        return this.design.weatherCity + ": " + Store.weatherName(this.sp.getInt("weatherCode", -1)) + String.format(Locale.US, ", %+.0f°", Float.valueOf(this.sp.getFloat("weatherTemp", 0.0f)));
    }

    private void findWeather(final String cityName) {
        if (cityName.trim().isEmpty()) {
            toast("Введите город");
            return;
        }
        if (this.weatherResult != null) {
            this.weatherResult.setText("Ищу…");
        }
        new Thread(new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda50
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.lambda$findWeather$89(cityName);
            }
        }).start();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$findWeather$89(String cityName) {
        try {
            final Store.Place p = Store.geocode(cityName);
            double[] wx = Store.fetchWeather(p.lat, p.lon);
            Store.saveWeather(this, wx);
            runOnUiThread(new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda32
                @Override // java.lang.Runnable
                public final void run() {
                    MainActivity.this.lambda$findWeather$87(p);
                }
            });
        } catch (Exception e) {
            final String msg = "Не получилось: " + e.getMessage();
            runOnUiThread(new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda33
                @Override // java.lang.Runnable
                public final void run() {
                    MainActivity.this.lambda$findWeather$88(msg);
                }
            });
        }
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$findWeather$87(Store.Place p) {
        this.design.weatherCity = p.name;
        this.design.weatherLat = p.lat;
        this.design.weatherLon = p.lon;
        this.design.attachRuntime(this);
        commit();
        if (this.weatherResult != null) {
            this.weatherResult.setText(weatherSummary());
        }
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$findWeather$88(String m) {
        if (this.weatherResult != null) {
            this.weatherResult.setText(m);
        }
    }

    private void buildMineTab() {
        LinearLayout c = this.ui.card(this.content, "Мои стили");
        c.addView(this.ui.label("Сохраните текущий вид экрана (часы, надписи, стикеры, эффекты), чтобы потом вернуться к нему одним нажатием. Фото и вырез в стиль не входят.", 12.0f, SUB, false));
        c.addView(this.ui.pill("Сохранить текущий стиль", true, new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda72
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.savePresetDialog();
            }
        }));
        JSONArray list = Store.presets(this);
        for (int i = 0; i < list.length(); i++) {
            final int idx = i;
            final JSONObject p = list.optJSONObject(i);
            if (p != null) {
                LinearLayout row = this.ui.hrow();
                row.setPadding(this.ui.dp(14), this.ui.dp(10), this.ui.dp(10), this.ui.dp(10));
                row.setBackground(this.ui.rr(-14935008, 20));
                row.setGravity(16);
                TextView name = this.ui.label(p.optString("name", "Стиль"), 15.0f, TEXT, false);
                row.addView(name, new LinearLayout.LayoutParams(0, -2, 1.0f));
                row.addView(this.ui.pill("Применить", true, new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda74
                    @Override // java.lang.Runnable
                    public final void run() {
                        MainActivity.this.lambda$buildMineTab$90(p);
                    }
                }), this.ui.wrap());
                row.addView(this.ui.space(6));
                row.addView(this.ui.pill("✕", false, new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda75
                    @Override // java.lang.Runnable
                    public final void run() {
                        MainActivity.this.lambda$buildMineTab$91(idx);
                    }
                }), this.ui.wrap());
                LinearLayout.LayoutParams lp = this.ui.match();
                lp.topMargin = this.ui.dp(8);
                this.content.addView(row, lp);
            }
        }
        LinearLayout r = this.ui.card(this.content, "Сброс");
        r.addView(this.ui.pill("Вернуть стандартные настройки", false, new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda76
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.lambda$buildMineTab$93();
            }
        }));
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildMineTab$90(JSONObject p) {
        JSONObject d = p.optJSONObject("design");
        if (d != null) {
            setDesign(Design.fromJson(d));
        }
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildMineTab$91(int idx) {
        Store.deletePreset(this, idx);
        showTab(5);
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildMineTab$93() {
        new AlertDialog.Builder(this, android.R.style.Theme.Material.Dialog.Alert).setMessage("Вернуть все настройки к стандартным? Фото и вырез останутся.").setPositiveButton("Вернуть", new DialogInterface.OnClickListener() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda9
            @Override // android.content.DialogInterface.OnClickListener
            public final void onClick(DialogInterface dialogInterface, int i) {
                MainActivity.this.lambda$buildMineTab$92(dialogInterface, i);
            }
        }).setNegativeButton("Отмена", (DialogInterface.OnClickListener) null).show();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildMineTab$92(DialogInterface d, int w) {
        setDesign(new Design());
    }

    /* JADX INFO: Access modifiers changed from: private */
    public void savePresetDialog() {
        final EditText et = this.ui.edit("Название стиля", false);
        et.setText("Стиль " + (Store.presets(this).length() + 1));
        new AlertDialog.Builder(this, android.R.style.Theme.Material.Dialog.Alert).setTitle("Сохранить стиль").setView(et).setPositiveButton("Сохранить", new DialogInterface.OnClickListener() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda78
            @Override // android.content.DialogInterface.OnClickListener
            public final void onClick(DialogInterface dialogInterface, int i) {
                MainActivity.this.lambda$savePresetDialog$94(et, dialogInterface, i);
            }
        }).setNegativeButton("Отмена", (DialogInterface.OnClickListener) null).show();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$savePresetDialog$94(EditText et, DialogInterface d, int w) {
        try {
            String name = et.getText().toString().trim();
            Store.addPreset(this, name.isEmpty() ? "Стиль" : name, this.design);
            showTab(5);
            toast("Стиль сохранён");
        } catch (Exception e) {
            toast("Не удалось сохранить");
        }
    }

    /* JADX INFO: Access modifiers changed from: private */
    public void applyWallpaper() {
        if (this.design.bgMode == 0 && !Store.hasBackground(this)) {
            toast("Сначала выберите фото или включите градиент");
            return;
        }
        Intent i = new Intent("android.service.wallpaper.CHANGE_LIVE_WALLPAPER");
        i.putExtra("android.service.wallpaper.extra.LIVE_WALLPAPER_COMPONENT", new ComponentName(this, (Class<?>) ClockWallpaperService.class));
        try {
            startActivity(i);
            toast("Нажмите «Установить» и отметьте экран блокировки");
        } catch (Exception e) {
            try {
                startActivity(new Intent("android.service.wallpaper.LIVE_WALLPAPER_CHOOSER"));
            } catch (Exception e2) {
                toast("Не удалось открыть выбор обоев");
            }
        }
    }

    private void pickImage(int req) {
        Intent i;
        if (Build.VERSION.SDK_INT >= 33) {
            i = new Intent("android.provider.action.PICK_IMAGES");
        } else {
            i = new Intent("android.intent.action.GET_CONTENT");
            i.setType("image/*");
            i.addCategory("android.intent.category.OPENABLE");
        }
        try {
            startActivityForResult(i, req);
        } catch (Exception e) {
            try {
                Intent j = new Intent("android.intent.action.GET_CONTENT");
                j.setType("image/*");
                j.addCategory("android.intent.category.OPENABLE");
                startActivityForResult(Intent.createChooser(j, "Выберите картинку"), req);
            } catch (Exception e2) {
                showError("Не нашёл, чем выбрать картинку", String.valueOf(e2.getMessage()));
            }
        }
    }

    private void pickFile(int req) {
        Intent i = new Intent("android.intent.action.GET_CONTENT");
        i.setType("*/*");
        i.addCategory("android.intent.category.OPENABLE");
        try {
            startActivityForResult(Intent.createChooser(i, "Выберите файл шрифта"), req);
        } catch (Exception e) {
            toast("Не нашёл, чем выбрать файл");
        }
    }

    private String displayName(Uri uri) {
        int i;
        try {
            Cursor c = getContentResolver().query(uri, null, null, null, null);
            if (c != null) {
                try {
                    if (c.moveToFirst() && (i = c.getColumnIndex("_display_name")) >= 0) {
                        String string = c.getString(i);
                        if (c != null) {
                            c.close();
                        }
                        return string;
                    }
                } finally {
                }
            }
            if (c != null) {
                c.close();
                return "Свой шрифт";
            }
            return "Свой шрифт";
        } catch (Exception e) {
            return "Свой шрифт";
        }
    }

    @Override // android.app.Activity
    protected void onActivityResult(final int req, int result, Intent data) {
        super.onActivityResult(req, result, data);
        if (result != -1 || data == null || data.getData() == null) {
            return;
        }
        final Uri uri = data.getData();
        toast("Обрабатываю…");
        new Thread(new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda49
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.lambda$onActivityResult$97(req, uri);
            }
        }).start();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$onActivityResult$97(int req, Uri uri) {
        String msg = null;
        String error = null;
        try {
            if (req == 3) {
                final String fid = Fonts.importFont(this, uri, displayName(uri));
                runOnUiThread(new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda6
                    @Override // java.lang.Runnable
                    public final void run() {
                        MainActivity.this.lambda$onActivityResult$95(fid);
                    }
                });
                msg = "Шрифт добавлен и выбран";
            } else if (req == 1) {
                Store.importBackground(this, uri);
                msg = "Фото загружено";
            } else {
                msg = Store.importCut(this, uri);
            }
        } catch (Exception e) {
            error = String.valueOf(e.getMessage());
        } catch (OutOfMemoryError e2) {
            error = "Не хватило памяти: картинка слишком большая";
        }
        final String m = msg;
        final String er = error;
        runOnUiThread(new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda7
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.lambda$onActivityResult$96(er, m);
            }
        });
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$onActivityResult$95(String fid) {
        Design.FontSpec target = this.design.fontOf(this.selectedId);
        if (target == null) {
            target = this.design.font;
        }
        target.id = fid;
        commit();
        rebuild();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$onActivityResult$96(String er, String m) {
        if (er != null) {
            showError("Не получилось", er);
        } else {
            toast(m);
        }
        refreshAfterImages();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public void runAutoCut() {
        if (!Store.hasBackground(this)) {
            toast("Сначала выберите фото");
        } else {
            toast("Вырезаю… это может занять до минуты");
            new Thread(new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda53
                @Override // java.lang.Runnable
                public final void run() {
                    MainActivity.this.lambda$runAutoCut$99();
                }
            }).start();
        }
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$runAutoCut$99() {
        String msg = null;
        String error = null;
        try {
            msg = Store.autoCut(this);
        } catch (Exception e) {
            error = String.valueOf(e.getMessage());
        } catch (Throwable e2) {
            error = e2.getClass().getSimpleName() + ": " + e2.getMessage();
        }
        final String m = msg;
        final String er = error;
        runOnUiThread(new Runnable() { // from class: com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda52
            @Override // java.lang.Runnable
            public final void run() {
                MainActivity.this.lambda$runAutoCut$98(er, m);
            }
        });
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$runAutoCut$98(String er, String m) {
        if (er != null) {
            showError("Автовырезание не получилось", er);
        } else {
            toast(m);
        }
        refreshAfterImages();
    }

    private void showError(String title, String message) {
        new AlertDialog.Builder(this, android.R.style.Theme.Material.Dialog.Alert).setTitle(title).setMessage(message).setPositiveButton("Понятно", (DialogInterface.OnClickListener) null).show();
    }

    private void toast(String s) {
        Toast.makeText(this, s, 1).show();
    }
}
