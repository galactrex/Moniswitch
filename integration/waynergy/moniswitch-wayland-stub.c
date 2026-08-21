// Copyright (c) 2026 Galactrex
// SPDX-License-Identifier: MIT

#define _GNU_SOURCE

#include <fcntl.h>
#include <signal.h>
#include <stdio.h>
#include <string.h>
#include <sys/mman.h>
#include <unistd.h>
#include <wayland-server-core.h>
#include <wayland-server-protocol.h>

static const char keymap[] =
    "xkb_keymap {\n"
    "xkb_keycodes { include \"evdev+aliases(qwerty)\" };\n"
    "xkb_types { include \"complete\" };\n"
    "xkb_compat { include \"complete\" };\n"
    "xkb_symbols { include \"pc+us+inet(evdev)\" };\n"
    "};\n";

static void keyboard_release(struct wl_client *client, struct wl_resource *resource)
{
    (void)client;
    wl_resource_destroy(resource);
}

static const struct wl_keyboard_interface keyboard_implementation = {
    .release = keyboard_release,
};

static int keymap_fd(void)
{
    size_t written = 0;
    int fd = memfd_create("moniswitch-keymap", MFD_CLOEXEC | MFD_ALLOW_SEALING);

    if (fd < 0) {
        return -1;
    }

    while (written < sizeof(keymap)) {
        ssize_t result = write(fd, keymap + written, sizeof(keymap) - written);
        if (result <= 0) {
            close(fd);
            return -1;
        }
        written += (size_t)result;
    }

    if (fcntl(
            fd,
            F_ADD_SEALS,
            F_SEAL_SEAL | F_SEAL_SHRINK | F_SEAL_GROW | F_SEAL_WRITE) < 0) {
        close(fd);
        return -1;
    }

    if (lseek(fd, 0, SEEK_SET) < 0) {
        close(fd);
        return -1;
    }

    return fd;
}

static void seat_get_pointer(
    struct wl_client *client,
    struct wl_resource *seat_resource,
    uint32_t id)
{
    (void)client;
    (void)id;
    wl_resource_post_error(seat_resource, 0, "Pointer access is unavailable");
}

static void seat_get_keyboard(
    struct wl_client *client,
    struct wl_resource *seat_resource,
    uint32_t id)
{
    struct wl_resource *keyboard_resource = wl_resource_create(
        client,
        &wl_keyboard_interface,
        wl_resource_get_version(seat_resource),
        id);
    int fd;

    if (keyboard_resource == NULL) {
        wl_client_post_no_memory(client);
        return;
    }

    wl_resource_set_implementation(
        keyboard_resource,
        &keyboard_implementation,
        NULL,
        NULL);

    fd = keymap_fd();
    if (fd < 0) {
        wl_resource_post_no_memory(keyboard_resource);
        return;
    }

    wl_keyboard_send_keymap(
        keyboard_resource,
        WL_KEYBOARD_KEYMAP_FORMAT_XKB_V1,
        fd,
        (uint32_t)sizeof(keymap));
    close(fd);

    if (wl_resource_get_version(keyboard_resource) >= WL_KEYBOARD_REPEAT_INFO_SINCE_VERSION) {
        wl_keyboard_send_repeat_info(keyboard_resource, 25, 600);
    }
}

static void seat_get_touch(
    struct wl_client *client,
    struct wl_resource *seat_resource,
    uint32_t id)
{
    (void)client;
    (void)id;
    wl_resource_post_error(seat_resource, 0, "Touch access is unavailable");
}

static void seat_release(struct wl_client *client, struct wl_resource *resource)
{
    (void)client;
    wl_resource_destroy(resource);
}

static const struct wl_seat_interface seat_implementation = {
    .get_pointer = seat_get_pointer,
    .get_keyboard = seat_get_keyboard,
    .get_touch = seat_get_touch,
    .release = seat_release,
};

static void bind_seat(
    struct wl_client *client,
    void *data,
    uint32_t version,
    uint32_t id)
{
    struct wl_resource *resource;
    uint32_t supported_version = version < 7 ? version : 7;

    (void)data;
    resource = wl_resource_create(client, &wl_seat_interface, supported_version, id);
    if (resource == NULL) {
        wl_client_post_no_memory(client);
        return;
    }

    wl_resource_set_implementation(resource, &seat_implementation, NULL, NULL);
    wl_seat_send_capabilities(resource, WL_SEAT_CAPABILITY_KEYBOARD);
    if (supported_version >= WL_SEAT_NAME_SINCE_VERSION) {
        wl_seat_send_name(resource, "moniswitch-bootstrap");
    }
}

static int terminate_display(int signal_number, void *data)
{
    (void)signal_number;
    wl_display_terminate(data);
    return 0;
}

int main(int argc, char **argv)
{
    const char *socket_name = argc > 1 ? argv[1] : "wayland-0";
    struct wl_display *display;
    struct wl_event_loop *event_loop;

    if (socket_name[0] == '\0' || strchr(socket_name, '/') != NULL) {
        fputs("Invalid Wayland socket name.\n", stderr);
        return 2;
    }

    display = wl_display_create();
    if (display == NULL) {
        fputs("Could not create the private Wayland display.\n", stderr);
        return 3;
    }

    if (wl_display_add_socket(display, socket_name) != 0) {
        perror("Could not create the private Wayland socket");
        wl_display_destroy(display);
        return 4;
    }

    if (wl_global_create(display, &wl_seat_interface, 7, NULL, bind_seat) == NULL) {
        fputs("Could not create the keyboard-map provider.\n", stderr);
        wl_display_destroy(display);
        return 5;
    }

    event_loop = wl_display_get_event_loop(display);
    if (wl_event_loop_add_signal(event_loop, SIGTERM, terminate_display, display) == NULL ||
        wl_event_loop_add_signal(event_loop, SIGINT, terminate_display, display) == NULL) {
        fputs("Could not register the shutdown handler.\n", stderr);
        wl_display_destroy(display);
        return 6;
    }

    wl_display_run(display);
    wl_display_destroy_clients(display);
    wl_display_destroy(display);
    return 0;
}
