package com.tioxsit.gatewayplus

import org.junit.Assert.*
import org.junit.Test

class ProfilesTest {
    private val good = "[Interface]\nPrivateKey = AAAA\nAddress = 10.0.0.2/32\nDNS = 1.1.1.1\nMTU = 1380\n\n[Peer]\nPublicKey = BBBB\nEndpoint = 203.0.113.5:51820\nAllowedIPs = 0.0.0.0/0\n"

    @Test fun validConfigPasses() = assertNull(Profiles.validate(good))

    @Test fun badConfigsAreRejected() {
        assertNotNull(Profiles.validate(""))
        assertNotNull(Profiles.validate(good + "PostUp = calc\n"))
        assertNotNull(Profiles.validate("[Interface]\nPrivateKey = a\n"))
        assertNotNull(Profiles.validate("[Interface]\nPrivateKey = a\n[Peer]\nPublicKey = b\n"))
        assertNotNull(Profiles.validate(good + "что-то непонятное\n"))
    }

    @Test fun gspProfileIsRecognizedAndRefusedWithAHint() {
        val gsp = "# GSP\n[Interface]\nPrivateKey = AAAA\nAddress = 10.78.0.2/32\nFEC = auto\n\n[Peer]\nPublicKey = BBBB\nEndpoint = 203.0.113.5:51821\nAllowedIPs = 0.0.0.0/0\n"
        assertTrue(Profiles.isGsp(gsp))
        assertFalse(Profiles.isGsp(good))
        assertTrue(Profiles.validate(gsp)!!.contains("GSP"))
    }

    @Test fun endpointHostAndNames() {
        assertEquals("203.0.113.5", Profiles.endpointHost(good))
        assertEquals("2001:db8::1", Profiles.endpointHost("[Peer]\nEndpoint = [2001:db8::1]:51820\n"))
        assertEquals("De_Fra", Profiles.safeName("De Fra!"))
        assertTrue(Profiles.validName(Profiles.safeName("Германия")))
        assertEquals("gateway", Profiles.safeName("!!!"))
    }

    @Test fun interfaceValuesAreEditedInPlace() {
        assertEquals("1380", Profiles.interfaceValue(good, "MTU"))
        val m = Profiles.withInterfaceValue(good, "MTU", "1400")
        assertEquals("1400", Profiles.interfaceValue(m, "MTU"))
        assertEquals(1, Regex("(?m)^MTU").findAll(m).count())
        assertTrue(m.indexOf("MTU = 1400") < m.indexOf("[Peer]"))
    }

    @Test fun excludedApplicationsAreAddedAndReplaced() {
        val a = Profiles.forTunnel(good, listOf("com.whatsapp", "com.whatsapp.w4b"))
        assertEquals("com.whatsapp,com.whatsapp.w4b", Profiles.interfaceValue(a, "ExcludedApplications"))
        assertTrue(a.indexOf("ExcludedApplications") < a.indexOf("[Peer]"))
        val b = Profiles.forTunnel(a, listOf("com.whatsapp"))
        assertEquals(1, Regex("(?m)^ExcludedApplications").findAll(b).count())
        val c = Profiles.forTunnel(b, emptyList())
        assertFalse(c.contains("ExcludedApplications"))
        assertNull(Profiles.validate(c))
    }

    @Test fun serversAreGroupedByAddress() {
        val de2 = good.replace("203.0.113.5", "203.0.113.5")
        val fi = good.replace("203.0.113.5", "198.51.100.7")
        val groups = Profiles.groupByServer(mapOf("de-wg" to good, "de-wg2" to de2, "fi" to fi))
        assertEquals(2, groups.size)
        val de = groups.first { it.first == "203.0.113.5" }
        assertEquals(listOf("de-wg", "de-wg2"), de.second)
        assertEquals(listOf("fi"), groups.first { it.first == "198.51.100.7" }.second)
    }
}
