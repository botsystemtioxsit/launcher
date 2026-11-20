package com.lockscreen.customplus;

import android.app.Activity;
import android.os.Bundle;
import android.view.ViewGroup;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import com.lockscreen.customplus.Design;
import com.lockscreen.customplus.PreviewView;
import java.util.List;
import java.util.function.IntConsumer;

/* loaded from: classes3.dex */
public class EditorActivity extends Activity {
    private Design design;
    private LinearLayout panel;
    private PreviewView preview;
    private String selectedId = "clock";
    private Ui ui;

    @Override // android.app.Activity
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        Fonts.init(this);
        this.ui = new Ui(this);
        this.design = Design.load(this);
        String sel = getIntent().getStringExtra("selected");
        if (sel != null && this.design.pos(sel) != null) {
            this.selectedId = sel;
        }
        getWindow().setStatusBarColor(-16119284);
        getWindow().setNavigationBarColor(-16119284);
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(1);
        root.setBackgroundColor(-16119284);
        LinearLayout top = this.ui.hrow();
        top.setPadding(this.ui.dp(14), this.ui.dp(8), this.ui.dp(14), this.ui.dp(4));
        top.addView(this.ui.pill("✕ Готово", true, new Runnable() { // from class: com.lockscreen.customplus.EditorActivity$$ExternalSyntheticLambda6
            @Override // java.lang.Runnable
            public final void run() {
                EditorActivity.this.finish();
            }
        }), this.ui.wrap());
        TextView hint = this.ui.label("  Двигайте · щипок — размер · два пальца — поворот", 12.0f, -6512986, false);
        top.addView(hint, this.ui.wrap());
        root.addView(top);
        int screenW = getResources().getDisplayMetrics().widthPixels;
        int screenH = getResources().getDisplayMetrics().heightPixels;
        int ph = Math.round(screenH * 0.58f);
        int pw = Math.round(ph / 2.1666667f);
        if (pw > screenW * 0.92f) {
            pw = Math.round(screenW * 0.92f);
            ph = Math.round(pw * 2.1666667f);
        }
        if (Store.editProfile(this) == 1) {
            // экран «на боку»: предпросмотр широкий
            pw = Math.round(screenW * 0.92f);
            ph = Math.round(pw / 2.1666667f);
        }
        this.preview = new PreviewView(this);
        this.preview.interactive = true;
        this.preview.design = this.design;
        this.preview.selected = this.selectedId;
        this.preview.listener = new PreviewView.Listener() { // from class: com.lockscreen.customplus.EditorActivity.1
            @Override // com.lockscreen.customplus.PreviewView.Listener
            public void onSelect(String id) {
                if (id != null && !id.equals(EditorActivity.this.selectedId)) {
                    EditorActivity.this.selectedId = id;
                    EditorActivity.this.buildPanel();
                }
            }

            @Override // com.lockscreen.customplus.PreviewView.Listener
            public void onMoved() {
                EditorActivity.this.buildPanel();
            }
        };
        LinearLayout.LayoutParams plp = new LinearLayout.LayoutParams(pw, ph);
        plp.gravity = 1;
        plp.setMargins(0, this.ui.dp(4), 0, this.ui.dp(6));
        root.addView(this.preview, plp);
        ScrollView sv = new ScrollView(this);
        this.panel = new LinearLayout(this);
        this.panel.setOrientation(1);
        this.panel.setPadding(this.ui.dp(14), 0, this.ui.dp(14), this.ui.dp(12));
        sv.addView(this.panel, new ViewGroup.LayoutParams(-1, -2));
        root.addView(sv, new LinearLayout.LayoutParams(-1, 0, 1.0f));
        setContentView(root);
        buildPanel();
    }

    private void commit() {
        this.design.save(this);
        this.preview.invalidate();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public void buildPanel() {
        this.panel.removeAllViews();
        if (this.design.pos(this.selectedId) == null) {
            this.selectedId = "clock";
        }
        this.preview.selected = this.selectedId;
        String id = this.selectedId;
        LinearLayout c = this.ui.card(this.panel, null);
        final List<String[]> els = this.design.elements();
        String[] names = new String[els.size()];
        int idx = 0;
        for (int i = 0; i < els.size(); i++) {
            names[i] = els.get(i)[1];
            if (els.get(i)[0].equals(id)) {
                idx = i;
            }
        }
        c.addView(this.ui.chips(names, idx, new IntConsumer() { // from class: com.lockscreen.customplus.EditorActivity$$ExternalSyntheticLambda0
            @Override // java.util.function.IntConsumer
            public final void accept(int i2) {
                EditorActivity.this.lambda$buildPanel$0(els, i2);
            }
        }));
        final Design.Xf xf = this.design.xf(id);
        c.addView(this.ui.slider("Наклон вперёд-назад", -80, 80, xf.rx, "°", new IntConsumer() { // from class: com.lockscreen.customplus.EditorActivity$$ExternalSyntheticLambda1
            @Override // java.util.function.IntConsumer
            public final void accept(int i2) {
                EditorActivity.this.lambda$buildPanel$1(xf, i2);
            }
        }));
        c.addView(this.ui.slider("Наклон влево-вправо", -80, 80, xf.ry, "°", new IntConsumer() { // from class: com.lockscreen.customplus.EditorActivity$$ExternalSyntheticLambda2
            @Override // java.util.function.IntConsumer
            public final void accept(int i2) {
                EditorActivity.this.lambda$buildPanel$2(xf, i2);
            }
        }));
        c.addView(this.ui.slider("Перекос", -60, 60, xf.skew, "", new IntConsumer() { // from class: com.lockscreen.customplus.EditorActivity$$ExternalSyntheticLambda3
            @Override // java.util.function.IntConsumer
            public final void accept(int i2) {
                EditorActivity.this.lambda$buildPanel$3(xf, i2);
            }
        }));
        c.addView(this.ui.slider("Поворот", -180, 180, xf.rot, "°", new IntConsumer() { // from class: com.lockscreen.customplus.EditorActivity$$ExternalSyntheticLambda4
            @Override // java.util.function.IntConsumer
            public final void accept(int i2) {
                EditorActivity.this.lambda$buildPanel$4(xf, i2);
            }
        }));
        c.addView(this.ui.pill("Сбросить поворот и наклон", false, new Runnable() { // from class: com.lockscreen.customplus.EditorActivity$$ExternalSyntheticLambda5
            @Override // java.lang.Runnable
            public final void run() {
                EditorActivity.this.lambda$buildPanel$5(xf);
            }
        }));
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildPanel$0(List els, int i) {
        this.selectedId = ((String[]) els.get(i))[0];
        buildPanel();
        this.preview.invalidate();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildPanel$1(Design.Xf xf, int v) {
        xf.rx = v;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildPanel$2(Design.Xf xf, int v) {
        xf.ry = v;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildPanel$3(Design.Xf xf, int v) {
        xf.skew = v;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildPanel$4(Design.Xf xf, int v) {
        xf.rot = v;
        commit();
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$buildPanel$5(Design.Xf xf) {
        xf.reset();
        commit();
        buildPanel();
    }
}
