package com.daynew.app.share

import org.junit.Assert.assertEquals
import org.junit.Test

class ShareTextParserTest {
    @Test
    fun `splits multi-line shared text into title and note`() {
        val parsed = ShareTextParser.parse(
            subject = null,
            sharedText = "  准备发版检查清单 \r\n\r\n1. 跑通单元测试\n2. 验证桌面小组件  ",
        )
        assertEquals("准备发版检查清单", parsed.title)
        assertEquals("1. 跑通单元测试\n2. 验证桌面小组件", parsed.note)
    }

    @Test
    fun `uses subject as title when both subject and text are present`() {
        val parsed = ShareTextParser.parse(
            subject = "会议纪要链接",
            sharedText = "https://example.com/notes/123",
        )
        assertEquals("会议纪要链接", parsed.title)
        assertEquals("https://example.com/notes/123", parsed.note)
    }

    @Test
    fun `handles empty inputs safely`() {
        val parsed = ShareTextParser.parse("   ", null)
        assertEquals("", parsed.title)
        assertEquals("", parsed.note)
    }
}
