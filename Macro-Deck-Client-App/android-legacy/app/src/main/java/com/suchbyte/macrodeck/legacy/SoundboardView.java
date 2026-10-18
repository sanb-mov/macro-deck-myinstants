package com.suchbyte.macrodeck.legacy;

import android.content.Context;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.ColorDrawable;
import android.graphics.drawable.StateListDrawable;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.HorizontalScrollView;
import android.widget.LinearLayout;
import android.widget.TextView;

/**
 * The soundboard folder: a grid of flat sound buttons and a bar underneath with the page navigation, stop and
 * the size of the grid. It looks like the soundboard of the desktop app.
 */
public class SoundboardView extends LinearLayout {

    public interface ActionListener {
        /**
         * @param action PLAY, PREVIOUS, NEXT, STOP, BACK, ROWS_UP, ROWS_DOWN, COLUMNS_UP, COLUMNS_DOWN or
         *               SEARCH (asks for the search text, nothing is sent yet)
         * @param index  the sound for PLAY, otherwise ignored
         */
        void onAction(String action, int index);
    }

    private static final int BACKGROUND = 0xFF1C1C1C;
    private static final int SOUND = 0xFF333333;
    private static final int SOUND_PRESSED = 0xFF4D4D4D;
    private static final int NAV = 0xFF0067CD;
    private static final int NAV_DIM = 0xFF262626;
    private static final int STOP = 0xFFBE3232;
    private static final int BACK = 0xFFB26B00;
    private static final int SEARCH = 0xFF00897B;
    private static final int HIGHLIGHT = 0xFFC88C00;

    private final LinearLayout grid;
    private final LinearLayout bar;
    private final float density;

    private ActionListener listener;

    public SoundboardView(Context context) {
        super(context);
        density = context.getResources().getDisplayMetrics().density;

        setOrientation(VERTICAL);
        setBackgroundColor(BACKGROUND);

        grid = new LinearLayout(context);
        grid.setOrientation(VERTICAL);
        grid.setPadding(dp(4), dp(4), dp(4), dp(4));
        addView(grid, new LayoutParams(LayoutParams.MATCH_PARENT, 0, 1f));

        // The bar scrolls sideways when the screen is too narrow for all of it
        HorizontalScrollView scroll = new HorizontalScrollView(context);
        scroll.setHorizontalScrollBarEnabled(false);
        bar = new LinearLayout(context);
        bar.setOrientation(HORIZONTAL);
        bar.setGravity(Gravity.CENTER_VERTICAL);
        bar.setPadding(dp(8), dp(6), dp(8), dp(8));
        scroll.addView(bar, new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        addView(scroll, new LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.WRAP_CONTENT));
    }

    public void setActionListener(ActionListener listener) {
        this.listener = listener;
    }

    /** Rebuilds the grid and the bar from what the host last sent. */
    public void update(MdClient client) {
        buildGrid(client);
        buildBar(client);
    }

    private void buildGrid(MdClient client) {
        grid.removeAllViews();

        int index = 0;
        for (int row = 0; row < client.soundRows; row++) {
            LinearLayout line = new LinearLayout(getContext());
            line.setOrientation(HORIZONTAL);
            grid.addView(line, new LayoutParams(LayoutParams.MATCH_PARENT, 0, 1f));

            for (int column = 0; column < client.soundColumns; column++) {
                LayoutParams params = new LayoutParams(0, LayoutParams.MATCH_PARENT, 1f);
                params.setMargins(dp(4), dp(4), dp(4), dp(4));

                if (index < client.soundNames.size()) {
                    final int soundIndex = index;
                    // In a search the best match comes first, highlighted
                    boolean best = index == 0 && client.soundsPage == 1 && client.soundQuery.length() > 0;
                    Button sound = best
                            ? flatButton(client.soundNames.get(index), HIGHLIGHT, HIGHLIGHT)
                            : flatButton(client.soundNames.get(index), SOUND, SOUND_PRESSED);
                    sound.setTextSize(13);
                    sound.setMaxLines(3);
                    sound.setOnClickListener(new OnClickListener() {
                        @Override
                        public void onClick(View v) {
                            fire("PLAY", soundIndex);
                        }
                    });
                    line.addView(sound, params);
                } else {
                    // Keeps the cells of a short last page the size of the others
                    line.addView(new View(getContext()), params);
                }
                index++;
            }
        }
    }

    private void buildBar(MdClient client) {
        bar.removeAllViews();

        addBarButton("< Back", BACK, "BACK");
        addBarButton("< Previous", client.soundsPage > 1 ? NAV : NAV_DIM, "PREVIOUS");

        TextView page = new TextView(getContext());
        page.setText("Page " + client.soundsPage);
        page.setTextColor(Color.WHITE);
        page.setTypeface(null, Typeface.BOLD);
        page.setTextSize(15);
        page.setGravity(Gravity.CENTER);
        page.setMinWidth(dp(64));
        bar.addView(page);

        addBarButton("Next >", client.soundsHasMore ? NAV : NAV_DIM, "NEXT");
        addBarButton("Stop sound", STOP, "STOP");

        addStepper("Rows", client.soundRows, "ROWS_DOWN", "ROWS_UP");
        addStepper("Columns", client.soundColumns, "COLUMNS_DOWN", "COLUMNS_UP");

        // Opens the search; while a search is active the button shows what was searched for
        String query = client.soundQuery;
        String label = query.length() == 0 ? "Search"
                : "\"" + (query.length() > 9 ? query.substring(0, 8) + "..." : query) + "\"";
        Button search = flatButton(label, query.length() == 0 ? SEARCH : HIGHLIGHT,
                query.length() == 0 ? SEARCH : HIGHLIGHT);
        search.setTextSize(14);
        search.setOnClickListener(new OnClickListener() {
            @Override
            public void onClick(View v) {
                fire("SEARCH", -1);
            }
        });
        LayoutParams searchParams = new LayoutParams(dp(112), dp(42));
        searchParams.setMargins(dp(20), 0, dp(4), 0);
        bar.addView(search, searchParams);
    }

    private void addBarButton(String text, int color, final String action) {
        Button button = flatButton(text, color, color);
        button.setTextSize(14);
        button.setOnClickListener(new OnClickListener() {
            @Override
            public void onClick(View v) {
                fire(action, -1);
            }
        });
        LayoutParams params = new LayoutParams(dp(112), dp(42));
        params.setMargins(dp(4), 0, dp(4), 0);
        bar.addView(button, params);
    }

    /** A value with a minus and a plus button. */
    private void addStepper(String label, int value, final String down, final String up) {
        TextView name = new TextView(getContext());
        name.setText(label);
        name.setTextColor(Color.WHITE);
        name.setTextSize(14);
        name.setPadding(dp(16), 0, dp(6), 0);
        bar.addView(name);

        Button minus = stepButton("-", down);
        bar.addView(minus, new LayoutParams(dp(38), dp(42)));

        TextView number = new TextView(getContext());
        number.setText(String.valueOf(value));
        number.setTextColor(Color.WHITE);
        number.setTypeface(null, Typeface.BOLD);
        number.setTextSize(16);
        number.setGravity(Gravity.CENTER);
        number.setMinWidth(dp(30));
        bar.addView(number);

        Button plus = stepButton("+", up);
        bar.addView(plus, new LayoutParams(dp(38), dp(42)));
    }

    private Button stepButton(String text, final String action) {
        Button button = flatButton(text, SOUND, SOUND_PRESSED);
        button.setTextSize(18);
        button.setOnClickListener(new OnClickListener() {
            @Override
            public void onClick(View v) {
                fire(action, -1);
            }
        });
        return button;
    }

    private Button flatButton(String text, int color, int pressedColor) {
        Button button = new Button(getContext());
        button.setText(text);
        button.setTextColor(Color.WHITE);
        button.setTypeface(null, Typeface.BOLD);
        button.setGravity(Gravity.CENTER);
        button.setPadding(dp(4), dp(4), dp(4), dp(4));

        StateListDrawable background = new StateListDrawable();
        background.addState(new int[]{android.R.attr.state_pressed}, new ColorDrawable(pressedColor == color
                ? lighten(color) : pressedColor));
        background.addState(new int[]{}, new ColorDrawable(color));
        button.setBackgroundDrawable(background);
        return button;
    }

    private static int lighten(int color) {
        int r = Math.min(255, Color.red(color) + 40);
        int g = Math.min(255, Color.green(color) + 40);
        int b = Math.min(255, Color.blue(color) + 40);
        return Color.rgb(r, g, b);
    }

    private void fire(String action, int index) {
        if (listener != null) {
            listener.onAction(action, index);
        }
    }

    private int dp(int value) {
        return (int) (value * density + 0.5f);
    }
}
