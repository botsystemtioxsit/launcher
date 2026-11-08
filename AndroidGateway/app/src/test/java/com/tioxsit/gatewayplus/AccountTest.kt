package com.tioxsit.gatewayplus

import org.junit.Assert.*
import org.junit.Test

class AccountTest {
    // Контрольный пример посчитан отдельно в Python: hashlib.pbkdf2_hmac('sha256', пароль, соль, 300000, 20) → base32.
    @Test fun keyAndIdMatchReference() {
        val key = Account.deriveKey("alex", "correct horse battery")
        assertEquals("GXZINBNA3BUD7OT4DC7AVDMXN2QF35FY", key)
        assertEquals("8332469a4198e109183e98a7", Account.idOf(key))
    }

    @Test fun nickIsNormalizedButPasswordIsNot() {
        assertEquals(Account.deriveKey("alex", "pw-123456"), Account.deriveKey("  ALEX ", "pw-123456"))
        assertNotEquals(Account.deriveKey("alex", "pw-123456"), Account.deriveKey("alex", "PW-123456"))
        assertNotEquals(Account.deriveKey("alex", "pw-123456"), Account.deriveKey("alexa", "pw-123456"))
    }

    @Test fun keyHasExpectedShape() {
        val k = Account.deriveKey("Саша", "пароль-пароль")
        assertTrue(Regex("^[A-Z2-7]{32}$").matches(k))
        assertTrue(Regex("^[0-9a-f]{24}$").matches(Account.idOf(k)))
    }

    @Test fun pbkdf2KnownVectorRfc7914() {
        // RFC 7914 §11: PBKDF2-HMAC-SHA256, P="passwd", S="salt", c=1, dkLen=64 → начало 55ac046e56e3089fec1691c22544b605
        val out = Account.pbkdf2("passwd".toByteArray(), "salt".toByteArray(), 1, 16)
        assertEquals("55ac046e56e3089fec1691c22544b605", out.joinToString("") { "%02x".format(it) })
    }

    @Test fun validation() {
        assertNotNull(Account.nickError("ab"))
        assertNotNull(Account.nickError("a".repeat(31)))
        assertNotNull(Account.nickError("bad\nnick"))
        assertNull(Account.nickError("Alex_01"))
        assertNotNull(Account.passwordError("12345"))
        assertNull(Account.passwordError("123456"))
    }

    @Test fun namesAndReasonsAreCleaned() {
        assertEquals("Alex X", Account.cleanName("  Alex‮\n  X  "))
        assertEquals(30, Account.cleanName("я".repeat(50)).length)
        assertEquals("a b", Account.cleanReason("a\r\nb‏"))
    }

    @Test fun deviceHashIsStableAndAnonymous() {
        val a = Account.deviceHash("abcdef0123456789")
        assertTrue(Regex("^[0-9a-f]{32}$").matches(a))
        assertEquals(a, Account.deviceHash("abcdef0123456789"))
        assertFalse(a.contains("abcdef0123456789"))
        assertTrue(Regex("^[0-9a-f]{32}$").matches(Account.deviceHash("")))
    }
}
