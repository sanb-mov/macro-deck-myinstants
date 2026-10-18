package com.suchbyte.macrodeck.legacy;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.DialogInterface;
import android.graphics.Color;
import android.os.Bundle;
import android.os.Handler;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.View;
import android.view.inputmethod.EditorInfo;
import android.view.WindowManager;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.TextView;
import android.widget.Toast;

/**
 * The deck itself: one connection, one grid, plus a status overlay for the
 * connecting / waiting / reconnecting states the web client shows as loading
 * spinners and the "connection lost" page.
 */
public class DeckActivity extends Activity implements MdClient.Listener, DeckView.InteractionListener {

    public static final String EXTRA_CONNECTION_ID = "connection_id";

    private static final long RECONNECT_DELAY_MS = 3000;

    private Store store;
    private Conn conn;
    private MdClient client;

    private DeckView deckView;
    private TextView statusView;
    private SoundboardView soundboardView;

    private final Handler handler = new Handler();
    private boolean finishing;

    private final Runnable reconnectRunnable = new Runnable() {
        @Override
        public void run() {
            if (finishing) {
                return;
            }
            startClient();
        }
    };

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        store = new Store(this);
        String id = getIntent().getStringExtra(EXTRA_CONNECTION_ID);
        conn = id == null ? null : store.find(id);
        if (conn == null) {
            Toast.makeText(this, "Connection not found", Toast.LENGTH_SHORT).show();
            finish();
            return;
        }

        if (store.getKeepAwake()) {
            // A deck the user stares at without touching must not sleep.
            getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        }
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_FULLSCREEN);

        FrameLayout root = new FrameLayout(this);
        root.setBackgroundColor(Color.BLACK);

        deckView = new DeckView(this);
        deckView.setInteractionListener(this);
        deckView.setLongPressDelay(store.getLongPressDelay());
        deckView.setVisibility(View.GONE);
        root.addView(deckView, new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT));

        statusView = new TextView(this);
        statusView.setTextColor(Color.WHITE);
        statusView.setTextSize(18);
        statusView.setGravity(Gravity.CENTER);
        statusView.setPadding(32, 32, 32, 32);
        root.addView(statusView, new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT));

        soundboardView = new SoundboardView(this);
        soundboardView.setVisibility(View.GONE);
        soundboardView.setActionListener(new SoundboardView.ActionListener() {
            @Override
            public void onAction(String action, int index) {
                if ("SEARCH".equals(action)) {
                    showSearchDialog();
                } else if (client != null) {
                    client.sendSoundboardAction(action, index);
                }
            }
        });
        root.addView(soundboardView, 1, new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT));

        setContentView(root);

        startClient();
    }

    private void startClient() {
        setStatus("Connecting to " + conn.host + ":" + conn.port + "...");
        client = new MdClient(conn, store.getClientId(), store.getSkipSslValidation(), this);
        deckView.setClient(client);
        client.connect();
    }

    private void setStatus(String text) {
        if (text == null) {
            statusView.setVisibility(View.GONE);
            deckView.setVisibility(View.VISIBLE);
        } else {
            statusView.setText(text);
            statusView.setVisibility(View.VISIBLE);
            deckView.setVisibility(View.GONE);
        }
        applyMode();
    }

    /** Asks what to search for in MyInstants and sends it to the host. */
    private void showSearchDialog() {
        if (client == null) {
            return;
        }

        final EditText input = new EditText(this);
        input.setSingleLine(true);
        input.setHint("Search MyInstants");
        input.setImeOptions(EditorInfo.IME_ACTION_SEARCH);
        input.setText(client.soundQuery);
        input.setSelection(input.getText().length());

        final AlertDialog dialog = new AlertDialog.Builder(this)
                .setTitle("Search MyInstants")
                .setView(input)
                .setPositiveButton("Search", new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface d, int which) {
                        submitSearch(input.getText().toString());
                    }
                })
                .setNeutralButton("Trending", new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface d, int which) {
                        submitSearch("");
                    }
                })
                .setNegativeButton("Cancel", null)
                .create();

        input.setOnEditorActionListener(new android.widget.TextView.OnEditorActionListener() {
            @Override
            public boolean onEditorAction(TextView v, int actionId, KeyEvent event) {
                submitSearch(input.getText().toString());
                dialog.dismiss();
                return true;
            }
        });

        dialog.getWindow().setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_STATE_VISIBLE);
        dialog.show();
    }

    private void submitSearch(String query) {
        if (client != null) {
            client.sendSoundboardSearch(query.trim());
        }
    }

    /** Shows the soundboard instead of the deck while the host serves a soundboard folder. */
    private void applyMode() {
        boolean statusShown = statusView.getVisibility() == View.VISIBLE;
        boolean soundboard = !statusShown && client != null && client.isSoundboard();
        soundboardView.setVisibility(soundboard ? View.VISIBLE : View.GONE);
        if (soundboard) {
            deckView.setVisibility(View.GONE);
        } else if (!statusShown) {
            deckView.setVisibility(View.VISIBLE);
        }
    }

    // ------------------------------------------------------- client callbacks

    @Override
    public void onSocketOpen() {
        setStatus("Waiting for the host to accept the connection...");
    }

    @Override
    public void onDeckReady() {
        setStatus(null);
    }

    @Override
    public void onConfigChanged(int rows, int columns, double buttonSpacing,
                                double buttonRadius, boolean buttonBackground) {
        deckView.invalidate();
    }

    @Override
    public void onButtonsChanged() {
        deckView.invalidate();
        applyMode();
    }

    @Override
    public void onSoundboardChanged() {
        soundboardView.update(client);
        applyMode();
    }

    @Override
    public void onDisconnected(int code, String reason, boolean clean) {
        deckView.cancelPress();

        if (finishing || clean) {
            finish();
            return;
        }

        setStatus("Connection lost.\n\nCode " + code
                + (reason == null || reason.length() == 0 ? "" : "\n" + reason)
                + "\n\nRetrying in 3 seconds...\nPress back to give up.");
        handler.postDelayed(reconnectRunnable, RECONNECT_DELAY_MS);
    }

    // --------------------------------------------------------------- input

    @Override
    public void onInteraction(String method, int x, int y) {
        if (client != null) {
            client.sendInteraction(method, x, y);
        }
    }

    @Override
    public void onSliderChange(MdSlider slider, double value) {
        if (client != null) {
            client.sendSliderChange(slider, value);
        }
    }

    @Override
    public void onSliderMute(MdSlider slider, boolean muted) {
        if (client != null) {
            client.sendSliderMute(slider, muted);
        }
    }

    @Override
    public void onBack() {
        if (client != null) {
            client.sendFolderBack();
        }
    }

    @Override
    public void onBackPressed() {
        // In a slider folder there is no button to leave with, so the back key walks
        // up the folder tree instead of closing the connection.
        if (client != null && client.isSoundboard()) {
            client.sendSoundboardAction("BACK", -1);
            return;
        }

        if (client != null && client.hasSliders() && client.canGoBack()) {
            client.sendFolderBack();
            return;
        }

        finishing = true;
        handler.removeCallbacks(reconnectRunnable);
        if (client != null) {
            client.disconnect();
        }
        super.onBackPressed();
    }

    @Override
    protected void onDestroy() {
        finishing = true;
        handler.removeCallbacks(reconnectRunnable);
        if (client != null) {
            client.disconnect();
            client.releaseBitmaps();
        }
        super.onDestroy();
    }
}
