/*
 * bridge-tty: puts a Linux terminal session from the git bridge daemon into a Windows console.
 *
 * Git Extensions under Wine hosts its Console tab in ConEmu, which can only run a Windows program.
 * This is that program. It connects to the daemon (eng/wine/git-bridge.cs) with the port and token
 * from GITEXT_GIT_BRIDGE_PORT and GITEXT_GIT_BRIDGE_TOKEN, asks for a pseudo-terminal session, and is
 * then nothing but a wire: terminal output is written to the console untouched with virtual terminal
 * processing on, so ConEmu does the emulation; key events are translated to the byte sequences an
 * xterm sends; window-size changes become resize frames. The exit code is the program's.
 *
 *   bridge-tty [program [args...]]      no program means the user's login shell
 *
 * The working directory is the console's, in Windows form; the daemon translates it.
 * GITEXT_GIT_BRIDGE_TTY_LOG names a file (a Linux path is taken to be on Z:) that receives the console
 * sizes seen at start and on every resize event, for diagnosing what the host reports.
 * Build: x86_64-w64-mingw32-gcc -O2 -municode -o bridge-tty.exe bridge-tty.c -lws2_32
 */
#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <shellapi.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

enum { FRAME_STDIN = 0, FRAME_STDIN_EOF = 1, FRAME_KILL = 2, FRAME_RESIZE = 3 };
enum { FRAME_STDOUT = 1, FRAME_STDERR = 2, FRAME_EXIT = 3, FRAME_ERROR = 4, FRAME_PID = 5 };

#ifndef ENABLE_VIRTUAL_TERMINAL_PROCESSING
#define ENABLE_VIRTUAL_TERMINAL_PROCESSING 0x0004
#endif
#ifndef DISABLE_NEWLINE_AUTO_RETURN
#define DISABLE_NEWLINE_AUTO_RETURN 0x0008
#endif

static SOCKET g_socket = INVALID_SOCKET;
static HANDLE g_console_out;
static HANDLE g_console_in;
static CRITICAL_SECTION g_send_lock;
static FILE *g_log;

static void log_sizes(const char *when, const CONSOLE_SCREEN_BUFFER_INFO *info, unsigned cols, unsigned rows)
{
    if (!g_log)
    {
        return;
    }
    fprintf(g_log, "%lu %s buffer=%dx%d window=(%d,%d)-(%d,%d) maxwindow=%dx%d sent=%ux%u\n",
            (unsigned long)GetTickCount(), when, info->dwSize.X, info->dwSize.Y,
            info->srWindow.Left, info->srWindow.Top, info->srWindow.Right, info->srWindow.Bottom,
            info->dwMaximumWindowSize.X, info->dwMaximumWindowSize.Y, cols, rows);
    fflush(g_log);
}

static void open_log(void)
{
    const char *path = getenv("GITEXT_GIT_BRIDGE_TTY_LOG");
    if (!path || !*path)
    {
        return;
    }
    char windows_path[1024];
    if (path[0] == '/')
    {
        snprintf(windows_path, sizeof windows_path, "Z:%s", path);
        for (char *p = windows_path; *p; p++)
        {
            if (*p == '/') *p = '\\';
        }
        path = windows_path;
    }
    g_log = fopen(path, "a");
}

static void die(const char *what)
{
    char message[256];
    snprintf(message, sizeof message, "bridge-tty: %s (error %lu)\r\n", what, (unsigned long)GetLastError());
    DWORD written;
    WriteFile(g_console_out ? g_console_out : GetStdHandle(STD_ERROR_HANDLE), message, (DWORD)strlen(message), &written, NULL);
    ExitProcess(1);
}

/* ---- sending ---- */

static void send_all(const void *data, int length)
{
    const char *p = data;
    while (length > 0)
    {
        int sent = send(g_socket, p, length, 0);
        if (sent <= 0)
        {
            ExitProcess(1); /* the daemon hung up; the session is over */
        }
        p += sent;
        length -= sent;
    }
}

static void send_frame(unsigned char kind, const void *payload, unsigned length)
{
    unsigned char header[5] = { kind, (unsigned char)length, (unsigned char)(length >> 8), (unsigned char)(length >> 16), (unsigned char)(length >> 24) };
    EnterCriticalSection(&g_send_lock);
    send_all(header, 5);
    if (length > 0)
    {
        send_all(payload, (int)length);
    }
    LeaveCriticalSection(&g_send_lock);
}

/*
 * The terminal's size. ConEmu keeps the console buffer exactly as wide as its window, so the buffer width is
 * the column count; the height is the visible window's, since the buffer holds the scrollback. Under Wine the
 * window rectangle catches up with the buffer a little after a resize event, so wait for it, briefly.
 */
static int terminal_size(const char *when, unsigned *cols, unsigned *rows)
{
    CONSOLE_SCREEN_BUFFER_INFO info;
    for (int attempt = 0; ; attempt++)
    {
        if (!GetConsoleScreenBufferInfo(g_console_out, &info))
        {
            return 0;
        }
        if (info.srWindow.Right - info.srWindow.Left + 1 == info.dwSize.X || attempt >= 25)
        {
            break;
        }
        Sleep(20);
    }
    *cols = (unsigned)info.dwSize.X;
    *rows = (unsigned)(info.srWindow.Bottom - info.srWindow.Top + 1);
    log_sizes(when, &info, *cols, *rows);
    return 1;
}

static void send_resize(void)
{
    unsigned cols, rows;
    if (!terminal_size("resize", &cols, &rows))
    {
        return;
    }
    unsigned char payload[4] = { (unsigned char)cols, (unsigned char)(cols >> 8), (unsigned char)rows, (unsigned char)(rows >> 8) };
    send_frame(FRAME_RESIZE, payload, 4);
}

/* ---- JSON header ---- */

typedef struct
{
    char *data;
    size_t length;
    size_t capacity;
} buffer_t;

static void buffer_append(buffer_t *b, const char *s, size_t n)
{
    if (b->length + n + 1 > b->capacity)
    {
        b->capacity = (b->length + n + 1) * 2;
        b->data = realloc(b->data, b->capacity);
        if (!b->data)
        {
            die("out of memory");
        }
    }
    memcpy(b->data + b->length, s, n);
    b->length += n;
    b->data[b->length] = 0;
}

static void buffer_append_str(buffer_t *b, const char *s)
{
    buffer_append(b, s, strlen(s));
}

static char *utf16_to_utf8(const wchar_t *s)
{
    int n = WideCharToMultiByte(CP_UTF8, 0, s, -1, NULL, 0, NULL, NULL);
    char *out = malloc((size_t)n);
    if (!out)
    {
        die("out of memory");
    }
    WideCharToMultiByte(CP_UTF8, 0, s, -1, out, n, NULL, NULL);
    return out;
}

static void buffer_append_json_string(buffer_t *b, const char *s)
{
    buffer_append_str(b, "\"");
    for (; *s; s++)
    {
        unsigned char c = (unsigned char)*s;
        char escaped[8];
        switch (c)
        {
        case '"': buffer_append_str(b, "\\\""); break;
        case '\\': buffer_append_str(b, "\\\\"); break;
        case '\n': buffer_append_str(b, "\\n"); break;
        case '\r': buffer_append_str(b, "\\r"); break;
        case '\t': buffer_append_str(b, "\\t"); break;
        default:
            if (c < 0x20)
            {
                snprintf(escaped, sizeof escaped, "\\u%04x", c);
                buffer_append_str(b, escaped);
            }
            else
            {
                buffer_append(b, (const char *)&c, 1);
            }
        }
    }
    buffer_append_str(b, "\"");
}

/*
 * The git variables the app sets for a command, such as the sequence editor that rewrites a rebase todo, go to the
 * daemon; the ones that carry Windows paths stay behind, as in the app's own client. The user's shell in the Console
 * tab sees none of them, since the app sets none when it starts a shell.
 */
static void append_forwarded_environment(buffer_t *b)
{
    static const char *const excluded[] = { "GIT_SSH", "GIT_EDITOR", "GIT_ASKPASS", "GIT_EXEC_PATH", "GIT_TEMPLATE_DIR", "GIT_CONFIG_SYSTEM", NULL };
    wchar_t *block = GetEnvironmentStringsW();
    int first = 1;
    for (wchar_t *entry = block; entry && *entry; entry += wcslen(entry) + 1)
    {
        if (*entry == L'=' || (_wcsnicmp(entry, L"GIT_", 4) != 0 && _wcsnicmp(entry, L"DFT_", 4) != 0))
        {
            continue;
        }
        wchar_t *equals = wcschr(entry, L'=');
        if (!equals)
        {
            continue;
        }
        *equals = 0;
        char *name = utf16_to_utf8(entry);
        char *value = utf16_to_utf8(equals + 1);
        *equals = L'=';
        int skip = 0;
        for (const char *const *e = excluded; *e; e++)
        {
            if (_stricmp(name, *e) == 0)
            {
                skip = 1;
            }
        }
        if (!skip)
        {
            if (!first)
            {
                buffer_append_str(b, ",");
            }
            first = 0;
            buffer_append_json_string(b, name);
            buffer_append_str(b, ":");
            buffer_append_json_string(b, value);
        }
        free(name);
        free(value);
    }
    if (block)
    {
        FreeEnvironmentStringsW(block);
    }
}

static void send_header(int argc, wchar_t **argv)
{
    const char *token = getenv("GITEXT_GIT_BRIDGE_TOKEN");
    if (!token)
    {
        die("GITEXT_GIT_BRIDGE_TOKEN is not set");
    }

    wchar_t cwd_w[MAX_PATH * 4];
    DWORD cwd_len = GetCurrentDirectoryW(sizeof cwd_w / sizeof cwd_w[0], cwd_w);
    char *cwd = cwd_len ? utf16_to_utf8(cwd_w) : NULL;

    unsigned cols = 80, rows = 24;
    terminal_size("start", &cols, &rows);

    buffer_t b = { 0 };
    char size[64];
    buffer_append_str(&b, "{\"token\":");
    buffer_append_json_string(&b, token);
    buffer_append_str(&b, ",\"cwd\":");
    buffer_append_json_string(&b, cwd ? cwd : "");
    if (argc > 1)
    {
        char *program = utf16_to_utf8(argv[1]);
        const char *base = strrchr(program, '\\');
        base = base ? base + 1 : program;
        /* the app names its git.exe; the daemon's git is meant */
        if (_stricmp(base, "git.exe") == 0 || _stricmp(base, "git") == 0)
        {
            strcpy(program, "git");
        }
        buffer_append_str(&b, ",\"program\":");
        buffer_append_json_string(&b, program);
        free(program);
    }
    buffer_append_str(&b, ",\"args\":[");
    for (int i = 2; i < argc; i++)
    {
        char *arg = utf16_to_utf8(argv[i]);
        if (i > 2)
        {
            buffer_append_str(&b, ",");
        }
        buffer_append_json_string(&b, arg);
        free(arg);
    }
    buffer_append_str(&b, "],\"env\":{");
    append_forwarded_environment(&b);
    snprintf(size, sizeof size, "},\"stdin\":true,\"pty\":true,\"cols\":%u,\"rows\":%u}\n", cols, rows);
    buffer_append_str(&b, size);
    send_all(b.data, (int)b.length);
    free(b.data);
    free(cwd);
}

/* ---- output: socket to console ---- */

static int recv_all(void *data, int length)
{
    char *p = data;
    while (length > 0)
    {
        int got = recv(g_socket, p, length, 0);
        if (got <= 0)
        {
            return 0;
        }
        p += got;
        length -= got;
    }
    return 1;
}

static unsigned long long g_frames, g_bytes, g_writes, g_write_ms;

static void log_stats(void)
{
    if (g_log)
    {
        fprintf(g_log, "%lu stats frames=%llu bytes=%llu writes=%llu write_ms=%llu\n", (unsigned long)GetTickCount(), g_frames, g_bytes, g_writes, g_write_ms);
        fflush(g_log);
    }
}

static DWORD WINAPI output_thread(LPVOID unused)
{
    (void)unused;
    unsigned char header[5];
    unsigned char *payload = NULL;
    unsigned capacity = 0;
    for (;;)
    {
        if (!recv_all(header, 5))
        {
            ExitProcess(1);
        }
        unsigned length = header[1] | (header[2] << 8) | (header[3] << 16) | ((unsigned)header[4] << 24);
        if (length > capacity)
        {
            capacity = length;
            payload = realloc(payload, capacity);
            if (!payload)
            {
                die("out of memory");
            }
        }
        if (length > 0 && !recv_all(payload, (int)length))
        {
            ExitProcess(1);
        }
        switch (header[0])
        {
        case FRAME_STDOUT:
        case FRAME_STDERR:
        case FRAME_ERROR:
        {
            unsigned offset = 0;
            g_frames++;
            g_bytes += length;
            if (g_log)
            {
                fprintf(g_log, "%lu frame %u bytes%s\n", (unsigned long)GetTickCount(), length, memchr(payload, 0x1b, length) ? " esc" : "");
            }
            while (offset < length)
            {
                DWORD written = 0;
                ULONGLONG t0 = GetTickCount64();
                if (!WriteFile(g_console_out, payload + offset, length - offset, &written, NULL))
                {
                    ExitProcess(1);
                }
                g_write_ms += GetTickCount64() - t0;
                g_writes++;
                offset += written;
            }
            if (header[0] == FRAME_ERROR)
            {
                ExitProcess(1);
            }
            break;
        }
        case FRAME_EXIT:
        {
            int code = length >= 4 ? (int)(payload[0] | (payload[1] << 8) | (payload[2] << 16) | ((unsigned)payload[3] << 24)) : 1;
            log_stats();
            ExitProcess((UINT)code);
        }
        default:
            break; /* pid: nothing to do with it here */
        }
    }
}

/* ---- input: console to socket ---- */

static void append_utf8(buffer_t *b, unsigned codepoint)
{
    char out[4];
    int n;
    if (codepoint < 0x80)
    {
        out[0] = (char)codepoint;
        n = 1;
    }
    else if (codepoint < 0x800)
    {
        out[0] = (char)(0xC0 | (codepoint >> 6));
        out[1] = (char)(0x80 | (codepoint & 0x3F));
        n = 2;
    }
    else if (codepoint < 0x10000)
    {
        out[0] = (char)(0xE0 | (codepoint >> 12));
        out[1] = (char)(0x80 | ((codepoint >> 6) & 0x3F));
        out[2] = (char)(0x80 | (codepoint & 0x3F));
        n = 3;
    }
    else
    {
        out[0] = (char)(0xF0 | (codepoint >> 18));
        out[1] = (char)(0x80 | ((codepoint >> 12) & 0x3F));
        out[2] = (char)(0x80 | ((codepoint >> 6) & 0x3F));
        out[3] = (char)(0x80 | (codepoint & 0x3F));
        n = 4;
    }
    buffer_append(b, out, (size_t)n);
}

/* xterm's modifier parameter: 1 + shift(1) + alt(2) + ctrl(4) */
static int modifier_parameter(DWORD state)
{
    int m = 1;
    if (state & SHIFT_PRESSED) m += 1;
    if (state & (LEFT_ALT_PRESSED | RIGHT_ALT_PRESSED)) m += 2;
    if (state & (LEFT_CTRL_PRESSED | RIGHT_CTRL_PRESSED)) m += 4;
    return m;
}

/* CSI <n> ~ style keys, and CSI <letter> style keys; both take xterm's ";modifier" parameter */
static void append_tilde_key(buffer_t *b, int number, int modifier)
{
    char seq[16];
    if (modifier > 1)
    {
        snprintf(seq, sizeof seq, "\x1b[%d;%d~", number, modifier);
    }
    else
    {
        snprintf(seq, sizeof seq, "\x1b[%d~", number);
    }
    buffer_append_str(b, seq);
}

static void append_letter_key(buffer_t *b, char letter, int modifier)
{
    char seq[16];
    if (modifier > 1)
    {
        snprintf(seq, sizeof seq, "\x1b[1;%d%c", modifier, letter);
    }
    else
    {
        snprintf(seq, sizeof seq, "\x1b[%c", letter);
    }
    buffer_append_str(b, seq);
}

/* returns 1 when the key was a special key with its own sequence */
static int append_special_key(buffer_t *b, const KEY_EVENT_RECORD *key)
{
    int m = modifier_parameter(key->dwControlKeyState);
    switch (key->wVirtualKeyCode)
    {
    case VK_UP: append_letter_key(b, 'A', m); return 1;
    case VK_DOWN: append_letter_key(b, 'B', m); return 1;
    case VK_RIGHT: append_letter_key(b, 'C', m); return 1;
    case VK_LEFT: append_letter_key(b, 'D', m); return 1;
    case VK_HOME: append_letter_key(b, 'H', m); return 1;
    case VK_END: append_letter_key(b, 'F', m); return 1;
    case VK_INSERT: append_tilde_key(b, 2, m); return 1;
    case VK_DELETE: append_tilde_key(b, 3, m); return 1;
    case VK_PRIOR: append_tilde_key(b, 5, m); return 1;
    case VK_NEXT: append_tilde_key(b, 6, m); return 1;
    case VK_F1: case VK_F2: case VK_F3: case VK_F4:
    {
        char seq[16];
        char letter = (char)('P' + (key->wVirtualKeyCode - VK_F1));
        if (m > 1)
        {
            snprintf(seq, sizeof seq, "\x1b[1;%d%c", m, letter);
        }
        else
        {
            snprintf(seq, sizeof seq, "\x1bO%c", letter);
        }
        buffer_append_str(b, seq);
        return 1;
    }
    case VK_F5: append_tilde_key(b, 15, m); return 1;
    case VK_F6: append_tilde_key(b, 17, m); return 1;
    case VK_F7: append_tilde_key(b, 18, m); return 1;
    case VK_F8: append_tilde_key(b, 19, m); return 1;
    case VK_F9: append_tilde_key(b, 20, m); return 1;
    case VK_F10: append_tilde_key(b, 21, m); return 1;
    case VK_F11: append_tilde_key(b, 23, m); return 1;
    case VK_F12: append_tilde_key(b, 24, m); return 1;
    case VK_BACK:
        if (key->dwControlKeyState & (LEFT_ALT_PRESSED | RIGHT_ALT_PRESSED))
        {
            buffer_append_str(b, "\x1b");
        }
        buffer_append_str(b, "\x7f");
        return 1;
    default:
        return 0;
    }
}

static void input_loop(void)
{
    INPUT_RECORD records[64];
    buffer_t b = { 0 };
    unsigned high_surrogate = 0;
    for (;;)
    {
        DWORD count = 0;
        if (!ReadConsoleInputW(g_console_in, records, 64, &count))
        {
            ExitProcess(1);
        }
        b.length = 0;
        int resized = 0;
        for (DWORD i = 0; i < count; i++)
        {
            const INPUT_RECORD *r = &records[i];
            if (r->EventType == WINDOW_BUFFER_SIZE_EVENT)
            {
                if (g_log)
                {
                    fprintf(g_log, "%lu event dwSize=%dx%d\n", (unsigned long)GetTickCount(), r->Event.WindowBufferSizeEvent.dwSize.X, r->Event.WindowBufferSizeEvent.dwSize.Y);
                }
                resized = 1;
                continue;
            }
            if (r->EventType != KEY_EVENT || !r->Event.KeyEvent.bKeyDown)
            {
                continue;
            }
            const KEY_EVENT_RECORD *key = &r->Event.KeyEvent;
            for (WORD repeat = 0; repeat < key->wRepeatCount; repeat++)
            {
                if (append_special_key(&b, key))
                {
                    continue;
                }
                unsigned ch = key->uChar.UnicodeChar;
                if (ch == 0)
                {
                    /* a modifier or dead key on its own; Ctrl+Space arrives as VK_SPACE with no character */
                    if (key->wVirtualKeyCode == VK_SPACE && (key->dwControlKeyState & (LEFT_CTRL_PRESSED | RIGHT_CTRL_PRESSED)))
                    {
                        buffer_append(&b, "\0", 1);
                    }
                    continue;
                }
                if (ch >= 0xD800 && ch < 0xDC00)
                {
                    high_surrogate = ch;
                    continue;
                }
                if (ch >= 0xDC00 && ch < 0xE000 && high_surrogate)
                {
                    ch = 0x10000 + ((high_surrogate - 0xD800) << 10) + (ch - 0xDC00);
                    high_surrogate = 0;
                }
                /* Alt with a character is ESC then the character, as in xterm's metaSendsEscape;
                   AltGr on European layouts sets both Ctrl and Alt and produces the character itself */
                DWORD alt = key->dwControlKeyState & (LEFT_ALT_PRESSED | RIGHT_ALT_PRESSED);
                DWORD ctrl = key->dwControlKeyState & (LEFT_CTRL_PRESSED | RIGHT_CTRL_PRESSED);
                if (alt && !ctrl)
                {
                    buffer_append_str(&b, "\x1b");
                }
                if (ch == '\b' && key->wVirtualKeyCode == VK_BACK)
                {
                    ch = 0x7f;
                }
                append_utf8(&b, ch);
            }
        }
        if (b.length > 0)
        {
            send_frame(FRAME_STDIN, b.data, (unsigned)b.length);
        }
        if (resized)
        {
            send_resize();
        }
    }
}

/* ---- main ---- */

static BOOL WINAPI on_console_event(DWORD event)
{
    /* Ctrl+C and Ctrl+Break arrive as key events with processed input off; the close button ends the
       process, which closes the socket, which makes the daemon hang the session up */
    return event == CTRL_C_EVENT || event == CTRL_BREAK_EVENT;
}

int wmain(int argc, wchar_t **argv)
{
    g_console_out = GetStdHandle(STD_OUTPUT_HANDLE);
    g_console_in = GetStdHandle(STD_INPUT_HANDLE);
    InitializeCriticalSection(&g_send_lock);
    open_log();

    const char *port_text = getenv("GITEXT_GIT_BRIDGE_PORT");
    if (!port_text)
    {
        die("GITEXT_GIT_BRIDGE_PORT is not set");
    }

    WSADATA wsa;
    if (WSAStartup(MAKEWORD(2, 2), &wsa) != 0)
    {
        die("WSAStartup failed");
    }
    g_socket = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (g_socket == INVALID_SOCKET)
    {
        die("socket failed");
    }
    struct sockaddr_in address;
    memset(&address, 0, sizeof address);
    address.sin_family = AF_INET;
    address.sin_port = htons((unsigned short)atoi(port_text));
    address.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    if (connect(g_socket, (struct sockaddr *)&address, sizeof address) != 0)
    {
        die("cannot connect to the git bridge");
    }
    BOOL no_delay = TRUE;
    setsockopt(g_socket, IPPROTO_TCP, TCP_NODELAY, (const char *)&no_delay, sizeof no_delay);

    /* the console is a terminal: raw input, escape sequences interpreted on output, UTF-8 both ways */
    DWORD in_mode = 0, out_mode = 0;
    GetConsoleMode(g_console_in, &in_mode);
    GetConsoleMode(g_console_out, &out_mode);
    SetConsoleMode(g_console_in, ENABLE_WINDOW_INPUT | ENABLE_EXTENDED_FLAGS);
    SetConsoleMode(g_console_out, ENABLE_PROCESSED_OUTPUT | ENABLE_VIRTUAL_TERMINAL_PROCESSING | DISABLE_NEWLINE_AUTO_RETURN);
    SetConsoleOutputCP(CP_UTF8);
    SetConsoleCP(CP_UTF8);
    SetConsoleCtrlHandler(on_console_event, TRUE);

    send_header(argc, argv);

    HANDLE thread = CreateThread(NULL, 0, output_thread, NULL, 0, NULL);
    if (!thread)
    {
        die("cannot start the output thread");
    }
    input_loop();
    return 0;
}
