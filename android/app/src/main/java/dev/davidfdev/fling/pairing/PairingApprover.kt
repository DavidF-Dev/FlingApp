package dev.davidfdev.fling.pairing

interface PairingApprover {
    suspend fun requestApproval(deviceName: String): Boolean
}
