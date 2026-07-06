package dev.davidfdev.fling.data

data class ClipItem(
    val type: String,
    val data: ByteArray,
    val timestamp: Long,
    val receivedAt: Long,
)
