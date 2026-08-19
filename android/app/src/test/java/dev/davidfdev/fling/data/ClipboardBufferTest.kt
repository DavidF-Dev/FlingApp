package dev.davidfdev.fling.data

import org.junit.Assert.assertEquals
import org.junit.Test
import java.util.concurrent.CountDownLatch
import java.util.concurrent.Executors

class ClipboardBufferTest {

    private fun item(label: String) = ClipItem(
        type = "text/plain",
        data = label.toByteArray(),
        timestamp = System.currentTimeMillis(),
        receivedAt = System.currentTimeMillis(),
    )

    @Test
    fun addAndRetrieve() {
        val buffer = ClipboardBuffer()
        buffer.add(item("a"))
        buffer.add(item("b"))

        val all = buffer.getAll()
        assertEquals(2, all.size)
        assertEquals("b", String(all[0].data))
        assertEquals("a", String(all[1].data))
    }

    @Test
    fun evictsOldestAtCapacity() {
        val buffer = ClipboardBuffer(capacity = 3)
        buffer.add(item("1"))
        buffer.add(item("2"))
        buffer.add(item("3"))
        buffer.add(item("4"))

        assertEquals(3, buffer.size())
        val labels = buffer.getAll().map { String(it.data) }
        assertEquals(listOf("4", "3", "2"), labels)
    }

    @Test
    fun removesOnlyTheGivenItem() {
        val buffer = ClipboardBuffer()
        val first = ClipItem("text/plain", "a".toByteArray(), 1000L, 1000L)
        val second = ClipItem("text/plain", "b".toByteArray(), 1000L, 1000L)
        buffer.add(first)
        buffer.add(second)

        buffer.remove(first)

        assertEquals(1, buffer.size())
        assertEquals("b", String(buffer.getAll().first().data))
    }

    @Test
    fun threadSafety() {
        val buffer = ClipboardBuffer(capacity = 50)
        val threads = 10
        val perThread = 20
        val latch = CountDownLatch(threads)
        val executor = Executors.newFixedThreadPool(threads)

        repeat(threads) { t ->
            executor.submit {
                repeat(perThread) { i ->
                    buffer.add(item("t${t}_i${i}"))
                }
                latch.countDown()
            }
        }
        latch.await()
        executor.shutdown()

        assertEquals(50, buffer.size())
    }
}
