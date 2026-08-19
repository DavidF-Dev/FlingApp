package dev.davidfdev.fling.data

import android.content.Context
import android.net.Uri
import androidx.core.content.FileProvider
import java.io.File

/// Disk cache holding the bytes behind the content URIs handed out for received images.
///
/// File names are derived from the clip's unique id, so a URI is never reused for
/// different content. Consumers that cache by URI — image loaders, share targets,
/// clipboard history — would otherwise keep serving the first image they saw.
object ClipImageCache {

    private const val DIR_NAME = "clip_images"

    fun write(context: Context, item: ClipItem): Uri {
        val file = fileFor(context, item)
        file.parentFile?.mkdirs()
        file.writeBytes(item.data)
        return uriFor(context, file)
    }

    fun delete(context: Context, item: ClipItem) {
        fileFor(context, item).delete()
    }

    fun clear(context: Context) {
        dir(context).deleteRecursively()
    }

    private fun dir(context: Context) = File(context.cacheDir, DIR_NAME)

    private fun fileFor(context: Context, item: ClipItem) = File(dir(context), "clip_${item.id}.png")

    private fun uriFor(context: Context, file: File): Uri =
        FileProvider.getUriForFile(context, "${context.packageName}.fileprovider", file)
}
