package com.tioxsit.gatewayplus

import android.content.Context
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.text.InputType
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.TextView

// Кирпичики интерфейса (всё рисуется кодом, как в лаунчере) в киберпанк-палитре проекта.
object Pal {
    val BG = Color.parseColor("#070314")
    val PANEL = Color.parseColor("#170E36")
    val LINE = Color.parseColor("#4A22A0")
    val TEXT = Color.parseColor("#EAF8FF")
    val MUTED = Color.parseColor("#A08FE0")
    val CYAN = Color.parseColor("#00F0FF")
    val PINK = Color.parseColor("#FF2E97")
    val GREEN = Color.parseColor("#3CFF9A")
    val RED = Color.parseColor("#FF3B5C")
    val YELLOW = Color.parseColor("#FCEE0A")
}

class Ui(private val ctx: Context) {
    fun dp(v: Int): Int = TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, v.toFloat(), ctx.resources.displayMetrics).toInt()

    private fun box(fill: Int, stroke: Int?, radiusDp: Int = 4): GradientDrawable = GradientDrawable().apply {
        setColor(fill)
        cornerRadius = dp(radiusDp).toFloat()
        if (stroke != null) setStroke(dp(1), stroke)
    }

    fun title(text: String) = TextView(ctx).apply {
        this.text = text; setTextColor(Pal.CYAN); typeface = Typeface.MONOSPACE; setTypeface(typeface, Typeface.BOLD)
        setTextSize(TypedValue.COMPLEX_UNIT_SP, 22f)
    }

    fun heading(text: String) = TextView(ctx).apply {
        this.text = text; setTextColor(Pal.TEXT); setTextSize(TypedValue.COMPLEX_UNIT_SP, 17f); setTypeface(typeface, Typeface.BOLD)
        setPadding(0, 0, 0, dp(10))
    }

    fun text(text: String, color: Int = Pal.TEXT, sp: Float = 14f, bold: Boolean = false) = TextView(ctx).apply {
        this.text = text; setTextColor(color); setTextSize(TypedValue.COMPLEX_UNIT_SP, sp)
        if (bold) setTypeface(typeface, Typeface.BOLD)
        setPadding(0, dp(2), 0, dp(2))
    }

    fun note(text: String, color: Int = Pal.MUTED) = text(text, color, 12f).apply { setPadding(0, dp(4), 0, dp(8)) }

    fun card(vararg views: View): LinearLayout = LinearLayout(ctx).apply {
        orientation = LinearLayout.VERTICAL
        background = box(Pal.PANEL, Pal.LINE)
        setPadding(dp(16), dp(14), dp(16), dp(12))
        for (v in views) addView(v)
        layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(12) }
    }

    // Вложенная рамка (карточка сервера внутри карточки «Мои серверы»); selected — обведена голубым.
    fun inner(selected: Boolean, vararg views: View): LinearLayout = LinearLayout(ctx).apply {
        orientation = LinearLayout.VERTICAL
        background = box(Color.parseColor("#1E1245"), if (selected) Pal.CYAN else Pal.LINE)
        setPadding(dp(12), dp(10), dp(12), dp(8))
        for (v in views) addView(v)
        layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(6); bottomMargin = dp(4) }
    }

    fun row(vararg views: View): LinearLayout = LinearLayout(ctx).apply {
        orientation = LinearLayout.HORIZONTAL
        for (v in views) addView(v)
    }

    // kind: "primary" (голубая заливка), "danger" (красная рамка), "plain"
    fun button(text: String, kind: String = "plain", onClick: () -> Unit): Button = Button(ctx).apply {
        this.text = text; isAllCaps = false
        setTextSize(TypedValue.COMPLEX_UNIT_SP, 14f)
        when (kind) {
            "primary" -> { setTextColor(Color.parseColor("#06050D")); background = box(Pal.CYAN, null); setTypeface(typeface, Typeface.BOLD) }
            "danger" -> { setTextColor(Pal.RED); background = box(Pal.PANEL, Pal.RED) }
            else -> { setTextColor(Pal.TEXT); background = box(Pal.PANEL, Pal.LINE) }
        }
        setPadding(dp(16), dp(10), dp(16), dp(10))
        minHeight = 0; minimumHeight = dp(44)
        layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { rightMargin = dp(8); topMargin = dp(4); bottomMargin = dp(4) }
        setOnClickListener { onClick() }
    }

    fun input(hint: String, password: Boolean = false, multiline: Boolean = false): EditText = EditText(ctx).apply {
        this.hint = hint
        setHintTextColor(Pal.MUTED); setTextColor(Pal.TEXT)
        background = box(Pal.BG, Pal.LINE)
        setPadding(dp(12), dp(10), dp(12), dp(10))
        inputType = when {
            password -> InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD
            multiline -> InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_MULTI_LINE
            else -> InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS
        }
        layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(6); bottomMargin = dp(6) }
    }

    fun gap(dpv: Int) = View(ctx).apply { layoutParams = LinearLayout.LayoutParams(1, dp(dpv)) }

    fun centered(v: TextView) = v.apply { gravity = Gravity.CENTER }
}
