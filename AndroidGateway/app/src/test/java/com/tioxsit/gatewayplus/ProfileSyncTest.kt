package com.tioxsit.gatewayplus

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class ProfileSyncTest {
    private val key = "GXZINBNA3BUD7OT4DC7AVDMXN2QF35FY"
    private val conf = "[Interface]\nPrivateKey = AAAA\n"

    // Контрольный пример посчитан отдельно (Python, библиотека cryptography) — тот же есть в самопроверке плагина для компьютера.
    @Test fun matchesComputerPlugin() {
        val nonce = ByteArray(12) { it.toByte() }
        val (n, c) = ProfileSync.encrypt(key, "de-wg", conf, nonce)
        assertEquals("AAECAwQFBgcICQoL", n)
        assertEquals("VQLPRa69jg9ZKd1fI+w8UAfFRfqzmmuk39sW83BLDbMeh7rl5m2/iUTt/cYS5A==", c)
        assertEquals(conf, ProfileSync.decrypt(key, "de-wg", n, c))
    }

    @Test fun wrongNameOrKeyDoesNotDecrypt() {
        val (n, c) = ProfileSync.encrypt(key, "de-wg", conf)
        assertNull(ProfileSync.decrypt(key, "other", n, c))
        assertNull(ProfileSync.decrypt("A" + key.substring(1), "de-wg", n, c))
        assertNull(ProfileSync.decrypt(key, "de-wg", "AAAA", c))
    }

    @Test fun nonceIsRandom() {
        val a = ProfileSync.encrypt(key, "x", conf)
        val b = ProfileSync.encrypt(key, "x", conf)
        assert(a.first != b.first)
    }
}
