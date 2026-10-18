package com.suchbyte.macrodeck.legacy;

import android.os.Handler;
import android.os.Looper;
import android.util.Base64;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.net.Socket;
import java.net.URI;
import java.security.MessageDigest;
import java.security.SecureRandom;
import java.security.cert.X509Certificate;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

import javax.net.ssl.HostnameVerifier;
import javax.net.ssl.HttpsURLConnection;
import javax.net.ssl.SSLContext;
import javax.net.ssl.SSLSocket;
import javax.net.ssl.SSLSocketFactory;
import javax.net.ssl.TrustManager;
import javax.net.ssl.X509TrustManager;

/**
 * Minimal RFC 6455 WebSocket client, written against the Java 6 / API 17 API
 * surface only.
 *
 * Why hand-rolled instead of a library: the WebView on Android 4.2 has no
 * WebSocket at all (it arrived with the Chromium WebView in 4.4), and every
 * maintained Java WebSocket library now compiles against Java 8 APIs
 * (java.util.function, java.time) that Android 4.2 cannot load. The protocol
 * surface Macro Deck actually needs is small: a text channel plus ping/pong.
 *
 * Callbacks are delivered on the main thread.
 */
public class Ws {

    public interface Listener {
        void onOpen();

        void onText(String text);

        /** Called exactly once, whatever the cause (clean close, error, drop). */
        void onClose(int code, String reason);
    }

    /** Guards against a buggy peer trying to allocate the whole heap. */
    private static final int MAX_PAYLOAD = 16 * 1024 * 1024;

    private static final String GUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    private final URI uri;
    private final boolean skipSslValidation;
    private final Listener listener;
    private final Handler main = new Handler(Looper.getMainLooper());
    private final SecureRandom random = new SecureRandom();
    private final Object writeLock = new Object();

    private ExecutorService sender;
    private Thread reader;
    private Socket socket;
    private InputStream in;
    private OutputStream out;

    private volatile boolean open;
    private volatile boolean closeReported;

    public Ws(URI uri, boolean skipSslValidation, Listener listener) {
        this.uri = uri;
        this.skipSslValidation = skipSslValidation;
        this.listener = listener;
    }

    public void connect() {
        sender = Executors.newSingleThreadExecutor();
        reader = new Thread(new Runnable() {
            @Override
            public void run() {
                try {
                    openSocket();
                    handshake();
                    open = true;
                    main.post(new Runnable() {
                        @Override
                        public void run() {
                            listener.onOpen();
                        }
                    });
                    readLoop();
                    reportClose(1006, "Connection closed");
                } catch (Exception e) {
                    String message = e.getMessage();
                    reportClose(1006, message == null ? e.toString() : message);
                } finally {
                    closeQuietly();
                }
            }
        }, "ws-reader");
        reader.start();
    }

    /** Safe to call from the main thread: the write happens on the sender thread. */
    public void send(final String text) {
        final ExecutorService s = sender;
        if (s == null || s.isShutdown()) {
            return;
        }
        try {
            s.execute(new Runnable() {
                @Override
                public void run() {
                    try {
                        writeFrame(0x1, text.getBytes("UTF-8"));
                    } catch (Exception e) {
                        closeQuietly();
                    }
                }
            });
        } catch (RuntimeException ignored) {
            // Executor was shut down between the check and the submit.
        }
    }

    /** Requests a clean (code 1000) close. */
    public void close() {
        final ExecutorService s = sender;
        if (s != null && !s.isShutdown()) {
            try {
                s.execute(new Runnable() {
                    @Override
                    public void run() {
                        try {
                            byte[] payload = new byte[2];
                            payload[0] = (byte) (1000 >> 8);
                            payload[1] = (byte) 1000;
                            writeFrame(0x8, payload);
                        } catch (Exception ignored) {
                        }
                        closeQuietly();
                    }
                });
            } catch (RuntimeException ignored) {
                closeQuietly();
            }
        } else {
            closeQuietly();
        }
        reportClose(1000, "Closed by client");
    }

    public boolean isOpen() {
        return open;
    }

    // ---------------------------------------------------------------- socket

    private void openSocket() throws Exception {
        String scheme = uri.getScheme() == null ? "ws" : uri.getScheme().toLowerCase(Locale.US);
        boolean secure = scheme.equals("wss");
        String host = uri.getHost();
        int port = uri.getPort();
        if (port == -1) {
            port = secure ? 443 : 80;
        }

        Socket plain = new Socket();
        plain.setTcpNoDelay(true);
        plain.connect(new InetSocketAddress(host, port), 10000);

        if (secure) {
            SSLSocketFactory factory;
            if (skipSslValidation) {
                SSLContext context = SSLContext.getInstance("TLS");
                context.init(null, new TrustManager[]{new TrustEverything()}, new SecureRandom());
                factory = context.getSocketFactory();
            } else {
                factory = (SSLSocketFactory) SSLSocketFactory.getDefault();
            }

            SSLSocket ssl = (SSLSocket) factory.createSocket(plain, host, port, true);

            // On API 17 TLS 1.1/1.2 exist but are DISABLED by default (they were
            // only enabled by default in API 20). A modern Macro Deck host will
            // refuse TLS 1.0, so enable everything the device actually supports.
            try {
                List<String> protocols = new ArrayList<String>();
                for (String candidate : ssl.getSupportedProtocols()) {
                    if (candidate.startsWith("TLS") && !candidate.equals("SSLv2Hello")) {
                        protocols.add(candidate);
                    }
                }
                if (!protocols.isEmpty()) {
                    ssl.setEnabledProtocols(protocols.toArray(new String[protocols.size()]));
                }
            } catch (Exception ignored) {
            }

            ssl.startHandshake();

            if (!skipSslValidation) {
                // A raw SSLSocket does NOT verify the hostname on its own -- only
                // HttpsURLConnection does that. Without this check wss:// would
                // accept any certificate signed by any trusted CA, for any host.
                HostnameVerifier verifier = HttpsURLConnection.getDefaultHostnameVerifier();
                if (!verifier.verify(host, ssl.getSession())) {
                    throw new IOException("Certificate does not match host " + host
                            + ". Turn on \"Skip SSL validation\" in the settings if the host uses a self-signed certificate.");
                }
            }

            socket = ssl;
        } else {
            socket = plain;
        }

        in = socket.getInputStream();
        out = socket.getOutputStream();
    }

    private static class TrustEverything implements X509TrustManager {
        @Override
        public void checkClientTrusted(X509Certificate[] chain, String authType) {
        }

        @Override
        public void checkServerTrusted(X509Certificate[] chain, String authType) {
        }

        @Override
        public X509Certificate[] getAcceptedIssuers() {
            return new X509Certificate[0];
        }
    }

    // ------------------------------------------------------------- handshake

    private void handshake() throws Exception {
        byte[] nonce = new byte[16];
        random.nextBytes(nonce);
        String key = Base64.encodeToString(nonce, Base64.NO_WRAP);

        String path = uri.getRawPath();
        if (path == null || path.length() == 0) {
            path = "/";
        }
        if (uri.getRawQuery() != null) {
            path = path + "?" + uri.getRawQuery();
        }

        int port = uri.getPort();
        String hostHeader = port == -1 ? uri.getHost() : uri.getHost() + ":" + port;

        StringBuilder request = new StringBuilder();
        request.append("GET ").append(path).append(" HTTP/1.1\r\n");
        request.append("Host: ").append(hostHeader).append("\r\n");
        request.append("Upgrade: websocket\r\n");
        request.append("Connection: Upgrade\r\n");
        request.append("Sec-WebSocket-Key: ").append(key).append("\r\n");
        request.append("Sec-WebSocket-Version: 13\r\n");
        request.append("\r\n");

        out.write(request.toString().getBytes("UTF-8"));
        out.flush();

        String statusLine = readLine();
        if (statusLine == null || !statusLine.contains(" 101")) {
            throw new IOException("Host refused the WebSocket upgrade: "
                    + (statusLine == null ? "no response" : statusLine));
        }

        String accept = null;
        String header;
        while ((header = readLine()) != null && header.length() > 0) {
            int colon = header.indexOf(':');
            if (colon > 0 && header.substring(0, colon).trim().equalsIgnoreCase("Sec-WebSocket-Accept")) {
                accept = header.substring(colon + 1).trim();
            }
        }

        MessageDigest sha1 = MessageDigest.getInstance("SHA-1");
        String expected = Base64.encodeToString(sha1.digest((key + GUID).getBytes("UTF-8")), Base64.NO_WRAP);
        if (accept == null || !accept.equals(expected)) {
            throw new IOException("Host returned an invalid Sec-WebSocket-Accept header");
        }
    }

    /**
     * Reads one CRLF-terminated line straight off the socket stream. Deliberately
     * byte-at-a-time: a buffered reader would swallow the first frames that
     * follow the handshake into its own buffer.
     */
    private String readLine() throws IOException {
        ByteArrayOutputStream buffer = new ByteArrayOutputStream();
        int previous = -1;
        while (true) {
            int b = in.read();
            if (b == -1) {
                return buffer.size() == 0 ? null : buffer.toString("UTF-8");
            }
            if (previous == '\r' && b == '\n') {
                byte[] bytes = buffer.toByteArray();
                return new String(bytes, 0, bytes.length - 1, "UTF-8");
            }
            buffer.write(b);
            previous = b;
        }
    }

    // ----------------------------------------------------------------- frames

    private void readLoop() throws IOException {
        ByteArrayOutputStream continuation = null;
        int continuationOpcode = 0;

        while (true) {
            int b0 = readByte();
            int b1 = readByte();

            boolean fin = (b0 & 0x80) != 0;
            int opcode = b0 & 0x0F;
            boolean masked = (b1 & 0x80) != 0;
            long length = b1 & 0x7F;

            if (length == 126) {
                length = (readByte() << 8) | readByte();
            } else if (length == 127) {
                length = 0;
                for (int i = 0; i < 8; i++) {
                    length = (length << 8) | readByte();
                }
            }
            if (length < 0 || length > MAX_PAYLOAD) {
                throw new IOException("Frame too large (" + length + " bytes)");
            }

            byte[] mask = null;
            if (masked) {
                mask = new byte[4];
                readFully(mask);
            }

            byte[] payload = new byte[(int) length];
            readFully(payload);
            if (mask != null) {
                for (int i = 0; i < payload.length; i++) {
                    payload[i] = (byte) (payload[i] ^ mask[i & 3]);
                }
            }

            switch (opcode) {
                case 0x0: // continuation
                    if (continuation == null) {
                        throw new IOException("Continuation frame without a start frame");
                    }
                    continuation.write(payload);
                    if (fin) {
                        if (continuationOpcode == 0x1) {
                            deliver(new String(continuation.toByteArray(), "UTF-8"));
                        }
                        continuation = null;
                    }
                    break;
                case 0x1: // text
                case 0x2: // binary (Macro Deck never sends it; drained for correctness)
                    if (fin) {
                        if (opcode == 0x1) {
                            deliver(new String(payload, "UTF-8"));
                        }
                    } else {
                        continuation = new ByteArrayOutputStream();
                        continuation.write(payload);
                        continuationOpcode = opcode;
                    }
                    break;
                case 0x8: { // close
                    int code = 1005;
                    String reason = "";
                    if (payload.length >= 2) {
                        code = ((payload[0] & 0xFF) << 8) | (payload[1] & 0xFF);
                        if (payload.length > 2) {
                            reason = new String(payload, 2, payload.length - 2, "UTF-8");
                        }
                    }
                    try {
                        writeFrame(0x8, payload);
                    } catch (Exception ignored) {
                    }
                    reportClose(code, reason);
                    return;
                }
                case 0x9: // ping -> pong with the same payload
                    try {
                        writeFrame(0xA, payload);
                    } catch (Exception ignored) {
                    }
                    break;
                case 0xA: // pong
                    break;
                default:
                    throw new IOException("Unknown opcode " + opcode);
            }
        }
    }

    private void writeFrame(int opcode, byte[] payload) throws IOException {
        OutputStream stream = out;
        if (stream == null) {
            throw new IOException("Not connected");
        }

        ByteArrayOutputStream frame = new ByteArrayOutputStream(payload.length + 14);
        frame.write(0x80 | opcode);

        int length = payload.length;
        if (length < 126) {
            frame.write(0x80 | length);
        } else if (length < 65536) {
            frame.write(0x80 | 126);
            frame.write((length >> 8) & 0xFF);
            frame.write(length & 0xFF);
        } else {
            frame.write(0x80 | 127);
            for (int i = 7; i >= 0; i--) {
                frame.write((int) ((((long) length) >> (8 * i)) & 0xFF));
            }
        }

        // Client-to-server frames MUST be masked (RFC 6455 5.3); servers drop
        // unmasked ones.
        byte[] mask = new byte[4];
        random.nextBytes(mask);
        frame.write(mask, 0, 4);
        for (int i = 0; i < length; i++) {
            frame.write(payload[i] ^ mask[i & 3]);
        }

        synchronized (writeLock) {
            stream.write(frame.toByteArray());
            stream.flush();
        }
    }

    private int readByte() throws IOException {
        int b = in.read();
        if (b == -1) {
            throw new IOException("Connection closed by host");
        }
        return b;
    }

    private void readFully(byte[] buffer) throws IOException {
        int offset = 0;
        while (offset < buffer.length) {
            int read = in.read(buffer, offset, buffer.length - offset);
            if (read == -1) {
                throw new IOException("Connection closed by host");
            }
            offset += read;
        }
    }

    // ------------------------------------------------------------- callbacks

    private void deliver(final String text) {
        main.post(new Runnable() {
            @Override
            public void run() {
                listener.onText(text);
            }
        });
    }

    private void reportClose(final int code, final String reason) {
        if (closeReported) {
            return;
        }
        closeReported = true;
        open = false;
        main.post(new Runnable() {
            @Override
            public void run() {
                listener.onClose(code, reason);
            }
        });
    }

    private void closeQuietly() {
        open = false;
        try {
            if (socket != null) {
                socket.close();
            }
        } catch (Exception ignored) {
        }
        ExecutorService s = sender;
        if (s != null) {
            s.shutdown();
        }
    }
}
