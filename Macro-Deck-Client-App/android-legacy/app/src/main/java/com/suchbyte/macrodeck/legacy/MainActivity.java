package com.suchbyte.macrodeck.legacy;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.DialogInterface;
import android.content.Intent;
import android.os.Bundle;
import android.view.LayoutInflater;
import android.view.Menu;
import android.view.MenuItem;
import android.view.View;
import android.widget.AdapterView;
import android.widget.ArrayAdapter;
import android.widget.CheckBox;
import android.widget.EditText;
import android.widget.ListView;
import android.widget.TextView;
import android.widget.Toast;

import java.util.ArrayList;
import java.util.List;

/**
 * Connection list: the equivalent of the Ionic home page. Tap to connect, long
 * press to edit or delete.
 */
public class MainActivity extends Activity {

    private static final int MENU_SETTINGS = 1;

    /**
     * Auto-connect must fire once per app launch, not every time the deck is
     * closed -- otherwise backing out of the deck would immediately reopen it.
     */
    private static boolean autoConnectAttempted;

    private Store store;
    private ListView listView;
    private TextView emptyLabel;
    private List<Conn> connections = new ArrayList<Conn>();

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);

        store = new Store(this);
        listView = (ListView) findViewById(R.id.connectionList);
        emptyLabel = (TextView) findViewById(R.id.emptyLabel);

        findViewById(R.id.addButton).setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                showConnectionDialog(null);
            }
        });

        listView.setOnItemClickListener(new AdapterView.OnItemClickListener() {
            @Override
            public void onItemClick(AdapterView<?> parent, View view, int position, long id) {
                connect(connections.get(position));
            }
        });

        listView.setOnItemLongClickListener(new AdapterView.OnItemLongClickListener() {
            @Override
            public boolean onItemLongClick(AdapterView<?> parent, View view, int position, long id) {
                showItemMenu(connections.get(position));
                return true;
            }
        });
    }

    @Override
    protected void onResume() {
        super.onResume();
        refresh();

        if (!autoConnectAttempted) {
            autoConnectAttempted = true;
            Conn auto = store.findAutoConnect();
            if (auto != null) {
                connect(auto);
            }
        }
    }

    private void refresh() {
        connections = store.getConnections();

        List<String> labels = new ArrayList<String>();
        for (Conn conn : connections) {
            labels.add(conn.label() + (conn.autoConnect ? "  [auto]" : ""));
        }

        listView.setAdapter(new ArrayAdapter<String>(this,
                android.R.layout.simple_list_item_1, labels));

        boolean empty = connections.isEmpty();
        listView.setVisibility(empty ? View.GONE : View.VISIBLE);
        emptyLabel.setVisibility(empty ? View.VISIBLE : View.GONE);
    }

    private void connect(Conn conn) {
        Intent intent = new Intent(this, DeckActivity.class);
        intent.putExtra(DeckActivity.EXTRA_CONNECTION_ID, conn.id);
        startActivity(intent);
    }

    // -------------------------------------------------------------- dialogs

    private void showItemMenu(final Conn conn) {
        new AlertDialog.Builder(this)
                .setTitle(conn.label())
                .setItems(new String[]{"Connect", "Edit", "Delete"}, new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        if (which == 0) {
                            connect(conn);
                        } else if (which == 1) {
                            showConnectionDialog(conn);
                        } else {
                            confirmDelete(conn);
                        }
                    }
                })
                .show();
    }

    private void confirmDelete(final Conn conn) {
        new AlertDialog.Builder(this)
                .setTitle("Delete connection")
                .setMessage("Delete \"" + conn.label() + "\"?")
                .setNegativeButton("Cancel", null)
                .setPositiveButton("Delete", new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        store.delete(conn.id);
                        refresh();
                    }
                })
                .show();
    }

    /** @param existing the connection to edit, or null to create a new one. */
    private void showConnectionDialog(final Conn existing) {
        View view = LayoutInflater.from(this).inflate(R.layout.dialog_connection, null);

        final EditText nameField = (EditText) view.findViewById(R.id.nameField);
        final EditText hostField = (EditText) view.findViewById(R.id.hostField);
        final EditText portField = (EditText) view.findViewById(R.id.portField);
        final EditText tokenField = (EditText) view.findViewById(R.id.tokenField);
        final CheckBox sslField = (CheckBox) view.findViewById(R.id.sslField);
        final CheckBox autoConnectField = (CheckBox) view.findViewById(R.id.autoConnectField);

        final Conn conn = existing == null ? new Conn() : existing;
        nameField.setText(conn.name);
        hostField.setText(conn.host);
        portField.setText(String.valueOf(conn.port));
        tokenField.setText(conn.token);
        sslField.setChecked(conn.ssl);
        autoConnectField.setChecked(conn.autoConnect);

        new AlertDialog.Builder(this)
                .setTitle(existing == null ? "Add connection" : "Edit connection")
                .setView(view)
                .setNegativeButton("Cancel", null)
                .setPositiveButton("Save", new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        String host = hostField.getText().toString().trim();
                        if (host.length() == 0) {
                            Toast.makeText(MainActivity.this, "A host is required", Toast.LENGTH_SHORT).show();
                            return;
                        }

                        int port;
                        try {
                            port = Integer.parseInt(portField.getText().toString().trim());
                        } catch (NumberFormatException e) {
                            Toast.makeText(MainActivity.this, "Invalid port", Toast.LENGTH_SHORT).show();
                            return;
                        }
                        if (port < 1 || port > 65535) {
                            Toast.makeText(MainActivity.this, "Invalid port", Toast.LENGTH_SHORT).show();
                            return;
                        }

                        conn.name = nameField.getText().toString().trim();
                        conn.host = host;
                        conn.port = port;
                        conn.token = tokenField.getText().toString().trim();
                        conn.ssl = sslField.isChecked();
                        conn.autoConnect = autoConnectField.isChecked();

                        if (conn.autoConnect) {
                            clearOtherAutoConnect(conn.id);
                        }

                        store.upsert(conn);
                        refresh();
                    }
                })
                .show();
    }

    /** Only one connection may auto-connect, so turn the flag off everywhere else. */
    private void clearOtherAutoConnect(String keepId) {
        List<Conn> all = store.getConnections();
        boolean changed = false;
        for (Conn other : all) {
            if (!other.id.equals(keepId) && other.autoConnect) {
                other.autoConnect = false;
                changed = true;
            }
        }
        if (changed) {
            store.saveConnections(all);
        }
    }

    private void showSettingsDialog() {
        View view = LayoutInflater.from(this).inflate(R.layout.dialog_settings, null);

        final EditText longPressField = (EditText) view.findViewById(R.id.longPressField);
        final CheckBox skipSslField = (CheckBox) view.findViewById(R.id.skipSslField);
        final CheckBox keepAwakeField = (CheckBox) view.findViewById(R.id.keepAwakeField);

        longPressField.setText(String.valueOf(store.getLongPressDelay()));
        skipSslField.setChecked(store.getSkipSslValidation());
        keepAwakeField.setChecked(store.getKeepAwake());

        new AlertDialog.Builder(this)
                .setTitle("Settings")
                .setView(view)
                .setNegativeButton("Cancel", null)
                .setPositiveButton("Save", new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        try {
                            int delay = Integer.parseInt(longPressField.getText().toString().trim());
                            if (delay > 0) {
                                store.setLongPressDelay(delay);
                            }
                        } catch (NumberFormatException ignored) {
                        }
                        store.setSkipSslValidation(skipSslField.isChecked());
                        store.setKeepAwake(keepAwakeField.isChecked());
                    }
                })
                .show();
    }

    // ----------------------------------------------------------------- menu

    @Override
    public boolean onCreateOptionsMenu(Menu menu) {
        menu.add(Menu.NONE, MENU_SETTINGS, Menu.NONE, "Settings");
        return true;
    }

    @Override
    public boolean onOptionsItemSelected(MenuItem item) {
        if (item.getItemId() == MENU_SETTINGS) {
            showSettingsDialog();
            return true;
        }
        return super.onOptionsItemSelected(item);
    }
}
