package dev.davidfdev.fling.data

import java.util.UUID

data class ClipItem(
    val type: String,
    val data: ByteArray,
    val timestamp: Long,
    val receivedAt: Long,
    val id: String = UUID.randomUUID().toString(),
)
