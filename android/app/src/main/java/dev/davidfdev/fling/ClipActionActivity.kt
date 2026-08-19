package dev.davidfdev.fling

import android.app.Activity
import android.app.NotificationManager
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.widget.Toast

/// Invisible entry point behind the Copy and Share actions on a received-content notification.
///
/// Runs as an activity rather than a broadcast receiver so the clipboard write happens
/// while the app holds focus, and so the share sheet can be launched directly.
class ClipActionActivity : Activity() {

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        when (val payload = resolvePayload()) {
            null -> toast("This clip is no longer available")
            else -> if (intent.action == ACTION_SHARE) share(payload) else copy(payload)
        }

        if (intent.action == ACTION_TAP_COPY) {
            dismissNotification()
        }
        finish()
    }

    /// Reads the payload carried by the launching intent, rejecting an image whose
    /// cached bytes have since been discarded.
    private fun resolvePayload(): Payload? {
        val type = intent.getStringExtra(EXTRA_TYPE) ?: return null
        return when {
            type.startsWith("text/") -> intent.getStringExtra(EXTRA_TEXT)?.let { Payload.Text(it) }
            type == "image/png" -> intent.getStringExtra(EXTRA_URI)
                ?.let { Uri.parse(it) }
                ?.takeIf { isReadable(it) }
                ?.let { Payload.Image(it) }
            else -> null
        }
    }

    private fun isReadable(uri: Uri): Boolean = try {
        contentResolver.openInputStream(uri)?.use { true } ?: false
    } catch (_: Exception) {
        false
    }

    private fun copy(payload: Payload) {
        val clip = when (payload) {
            is Payload.Text -> ClipData.newPlainText("Fling", payload.text)
            is Payload.Image -> ClipData.newUri(contentResolver, "Fling", payload.uri)
        }
        getSystemService(ClipboardManager::class.java).setPrimaryClip(clip)
        toast("Copied to clipboard")
    }

    private fun share(payload: Payload) {
        val send = Intent(Intent.ACTION_SEND).apply {
            when (payload) {
                is Payload.Text -> {
                    type = "text/plain"
                    putExtra(Intent.EXTRA_TEXT, payload.text)
                }
                is Payload.Image -> {
                    type = "image/png"
                    putExtra(Intent.EXTRA_STREAM, payload.uri)
                    addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                }
            }
        }
        startActivity(Intent.createChooser(send, null))
    }

    private fun dismissNotification() {
        val notificationId = intent.getIntExtra(EXTRA_NOTIFICATION_ID, -1)
        if (notificationId != -1) {
            getSystemService(NotificationManager::class.java).cancel(notificationId)
        }
    }

    private fun toast(message: String) {
        Toast.makeText(this, message, Toast.LENGTH_SHORT).show()
    }

    private sealed interface Payload {
        data class Text(val text: String) : Payload
        data class Image(val uri: Uri) : Payload
    }

    companion object {
        const val ACTION_TAP_COPY = "dev.davidfdev.fling.TAP_COPY_CLIP"
        const val ACTION_COPY = "dev.davidfdev.fling.COPY_CLIP"
        const val ACTION_SHARE = "dev.davidfdev.fling.SHARE_CLIP"
        const val EXTRA_NOTIFICATION_ID = "notification_id"
        const val EXTRA_TYPE = "clip_type"
        const val EXTRA_TEXT = "clip_text"
        const val EXTRA_URI = "clip_uri"
    }
}
