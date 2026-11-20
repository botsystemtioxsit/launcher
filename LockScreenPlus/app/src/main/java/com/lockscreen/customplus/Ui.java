package com.lockscreen.customplus;

import android.content.Context;
import android.content.res.ColorStateList;
import android.graphics.drawable.GradientDrawable;
import android.text.TextWatcher;
import android.view.View;
import android.widget.CompoundButton;
import android.widget.EditText;
import android.widget.HorizontalScrollView;
import android.widget.LinearLayout;
import android.widget.SeekBar;
import android.widget.Switch;
import android.widget.TextView;
import java.util.ArrayList;
import java.util.List;
import java.util.function.Consumer;
import java.util.function.IntConsumer;

/* loaded from: classes3.dex */
final class Ui {
    static final int ACCENT = -12676609;
    static final int BG = -16119284;
    static final int CARD = -14935008;
    static final int CHIP = -14013647;
    static final int SUB = -6512986;
    static final int TEXT = -723722;
    final Context ctx;

    Ui(Context ctx) {
        this.ctx = ctx;
    }

    int dp(int v) {
        return Math.round(v * this.ctx.getResources().getDisplayMetrics().density);
    }

    GradientDrawable rr(int color, int radiusDp) {
        GradientDrawable g = new GradientDrawable();
        g.setColor(color);
        g.setCornerRadius(dp(radiusDp));
        return g;
    }

    TextView label(String s, float size, int color, boolean bold) {
        TextView t = new TextView(this.ctx);
        t.setText(s);
        t.setTextSize(2, size);
        t.setTextColor(color);
        if (bold) {
            t.setTypeface(t.getTypeface(), 1);
        }
        t.setPadding(0, dp(4), 0, dp(2));
        return t;
    }

    LinearLayout hrow() {
        LinearLayout r = new LinearLayout(this.ctx);
        r.setOrientation(0);
        r.setGravity(16);
        return r;
    }

    LinearLayout.LayoutParams wrap() {
        return new LinearLayout.LayoutParams(-2, -2);
    }

    LinearLayout.LayoutParams match() {
        return new LinearLayout.LayoutParams(-1, -2);
    }

    View space(int dpW) {
        View v = new View(this.ctx);
        v.setLayoutParams(new LinearLayout.LayoutParams(dp(dpW), 1));
        return v;
    }

    LinearLayout card(LinearLayout parent, String title) {
        LinearLayout c = new LinearLayout(this.ctx);
        c.setOrientation(1);
        c.setPadding(dp(16), dp(12), dp(16), dp(14));
        c.setBackground(rr(CARD, 26));
        if (title != null) {
            c.addView(label(title, 16.0f, TEXT, true));
        }
        LinearLayout.LayoutParams lp = match();
        lp.topMargin = dp(10);
        parent.addView(c, lp);
        return c;
    }

    TextView pill(String s, boolean primary, final Runnable onClick) {
        TextView t = label(s, 14.0f, primary ? -1 : TEXT, true);
        t.setGravity(17);
        t.setPadding(dp(16), dp(10), dp(16), dp(10));
        t.setBackground(rr(primary ? ACCENT : CHIP, 22));
        t.setOnClickListener(new View.OnClickListener() { // from class: com.lockscreen.customplus.Ui$$ExternalSyntheticLambda0
            @Override // android.view.View.OnClickListener
            public final void onClick(View view) {
                onClick.run();
            }
        });
        return t;
    }

    View sw(String label, boolean value, final Consumer<Boolean> onChange) {
        Switch s = new Switch(this.ctx);
        s.setText(label);
        s.setTextColor(TEXT);
        s.setTextSize(2, 15.0f);
        s.setChecked(value);
        s.setPadding(0, dp(8), 0, dp(8));
        int[][] states = {new int[]{android.R.attr.state_checked}, new int[0]};
        s.setThumbTintList(new ColorStateList(states, new int[]{-1, -5197126}));
        s.setTrackTintList(new ColorStateList(states, new int[]{ACCENT, -12960958}));
        s.setOnCheckedChangeListener(new CompoundButton.OnCheckedChangeListener() { // from class: com.lockscreen.customplus.Ui$$ExternalSyntheticLambda1
            @Override // android.widget.CompoundButton.OnCheckedChangeListener
            public final void onCheckedChanged(CompoundButton compoundButton, boolean z) {
                onChange.accept(Boolean.valueOf(z));
            }
        });
        return s;
    }

    View slider(String name, final int min, int max, int value, final String unit, final IntConsumer onChange) {
        LinearLayout box = new LinearLayout(this.ctx);
        box.setOrientation(1);
        LinearLayout head = hrow();
        TextView n = label(name, 14.0f, TEXT, false);
        final TextView v = label(Math.max(min, Math.min(max, value)) + unit, 14.0f, SUB, false);
        head.addView(n, new LinearLayout.LayoutParams(0, -2, 1.0f));
        head.addView(v);
        SeekBar bar = new SeekBar(this.ctx);
        bar.setMax(max - min);
        bar.setProgress(Math.max(0, Math.min(max, value) - min));
        bar.setProgressTintList(ColorStateList.valueOf(ACCENT));
        bar.setThumbTintList(ColorStateList.valueOf(-1));
        bar.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener() { // from class: com.lockscreen.customplus.Ui.1
            @Override // android.widget.SeekBar.OnSeekBarChangeListener
            public void onProgressChanged(SeekBar b, int progress, boolean fromUser) {
                v.setText((min + progress) + unit);
                if (fromUser) {
                    onChange.accept(min + progress);
                }
            }

            @Override // android.widget.SeekBar.OnSeekBarChangeListener
            public void onStartTrackingTouch(SeekBar b) {
            }

            @Override // android.widget.SeekBar.OnSeekBarChangeListener
            public void onStopTrackingTouch(SeekBar b) {
            }
        });
        box.addView(head);
        box.addView(bar);
        return box;
    }

    View chips(String[] names, int selected, final IntConsumer onPick) {
        HorizontalScrollView hs = new HorizontalScrollView(this.ctx);
        hs.setHorizontalScrollBarEnabled(false);
        LinearLayout row = hrow();
        final List<TextView> items = new ArrayList<>();
        for (int i = 0; i < names.length; i++) {
            final int idx = i;
            TextView t = label(names[i], 14.0f, TEXT, false);
            t.setPadding(dp(16), dp(9), dp(16), dp(9));
            items.add(t);
            t.setOnClickListener(new View.OnClickListener() { // from class: com.lockscreen.customplus.Ui$$ExternalSyntheticLambda2
                @Override // android.view.View.OnClickListener
                public final void onClick(View view) {
                    Ui.this.lambda$chips$2(items, idx, onPick, view);
                }
            });
            LinearLayout.LayoutParams lp = wrap();
            lp.rightMargin = dp(6);
            lp.topMargin = dp(4);
            lp.bottomMargin = dp(4);
            row.addView(t, lp);
        }
        styleChips(items, selected);
        hs.addView(row);
        return hs;
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$chips$2(List items, int idx, IntConsumer onPick, View v) {
        styleChips(items, idx);
        onPick.accept(idx);
    }

    void styleChips(List<TextView> items, int selected) {
        int i = 0;
        while (i < items.size()) {
            TextView t = items.get(i);
            boolean on = i == selected;
            t.setBackground(rr(on ? ACCENT : CHIP, 20));
            t.setTextColor(on ? -1 : TEXT);
            i++;
        }
    }

    View swatches(final int[] colors, int selected, final IntConsumer onPick) {
        HorizontalScrollView horizontalScrollView = new HorizontalScrollView(this.ctx);
        horizontalScrollView.setHorizontalScrollBarEnabled(false);
        LinearLayout row = hrow();
        final List<View> items = new ArrayList<>();
        for (final int col : colors) {
            View v = new View(this.ctx);
            items.add(v);
            v.setOnClickListener(new View.OnClickListener() { // from class: com.lockscreen.customplus.Ui$$ExternalSyntheticLambda3
                @Override // android.view.View.OnClickListener
                public final void onClick(View view) {
                    Ui.this.lambda$swatches$3(items, colors, col, onPick, view);
                }
            });
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(dp(38), dp(38));
            lp.rightMargin = dp(8);
            lp.topMargin = dp(6);
            lp.bottomMargin = dp(6);
            row.addView(v, lp);
        }
        styleSwatches(items, colors, selected);
        horizontalScrollView.addView(row);
        return horizontalScrollView;
    }

    /* JADX INFO: Access modifiers changed from: private */
    public /* synthetic */ void lambda$swatches$3(List items, int[] colors, int col, IntConsumer onPick, View x) {
        styleSwatches(items, colors, col);
        onPick.accept(col);
    }

    private void styleSwatches(List<View> items, int[] colors, int selected) {
        for (int i = 0; i < items.size(); i++) {
            GradientDrawable g = new GradientDrawable();
            g.setShape(1);
            g.setColor(colors[i]);
            boolean on = colors[i] == selected;
            g.setStroke(dp(on ? 3 : 1), on ? ACCENT : -10855581);
            items.get(i).setBackground(g);
        }
    }

    EditText edit(String hint, boolean multiline) {
        EditText e = new EditText(this.ctx);
        e.setHint(hint);
        e.setTextColor(TEXT);
        e.setHintTextColor(-9539464);
        e.setTextSize(2, 15.0f);
        e.setBackground(rr(CHIP, 16));
        e.setPadding(dp(14), dp(10), dp(14), dp(10));
        if (multiline) {
            e.setInputType(131073);
            e.setMinLines(2);
            e.setGravity(48);
        } else {
            e.setInputType(17);
            e.setSingleLine(true);
        }
        LinearLayout.LayoutParams lp = match();
        lp.topMargin = dp(6);
        lp.bottomMargin = dp(6);
        e.setLayoutParams(lp);
        return e;
    }

    static abstract class SimpleWatcher implements TextWatcher {
        SimpleWatcher() {
        }

        @Override // android.text.TextWatcher
        public void beforeTextChanged(CharSequence s, int a, int b, int c) {
        }

        @Override // android.text.TextWatcher
        public void onTextChanged(CharSequence s, int a, int b, int c) {
        }
    }
}
