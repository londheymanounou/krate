package app.krate

import com.sun.jna.Library
import com.sun.jna.Native
import com.sun.jna.Pointer
import com.sun.jna.Structure

@Structure.FieldOrder("ok", "text")
open class KrateResult : Structure() {
    @JvmField var ok: Int = 0
    @JvmField var text: Pointer? = null

    class ByValue : KrateResult(), Structure.ByValue
}

interface KrateCore : Library {
    fun krate_run(id: String, input: String): KrateResult.ByValue
    fun krate_set_language(tag: String)
    fun krate_set_runtime(text: String)
    fun krate_tool_count(): Int
    fun krate_tool_id(index: Int): Pointer?
    fun krate_tool_name(index: Int): Pointer?
    fun krate_password(length: Long, upper: Boolean, lower: Boolean, digits: Boolean, symbols: Boolean): Pointer?
    fun krate_tool_category(index: Int): Pointer?
    fun krate_tool_category_name(index: Int): Pointer?
    fun krate_currency_store_rates(base: String, json: String): Int
    fun krate_free(text: Pointer?)
}

object Core {
    init {
        System.setProperty("jna.encoding", "UTF8")
    }
    private val lib: KrateCore = Native.load("krate_core", KrateCore::class.java)

    data class Result(val ok: Boolean, val text: String)

    fun run(id: String, input: String): Result {
        val res = lib.krate_run(id, input)
        val text = res.text?.getString(0, "UTF-8") ?: ""
        lib.krate_free(res.text)
        return Result(res.ok == 1, text)
    }

    fun setLanguage(tag: String) {
        lib.krate_set_language(tag)
    }

    fun setRuntime(text: String) {
        lib.krate_set_runtime(text)
    }

    fun toolCount(): Int {
        return lib.krate_tool_count()
    }

    fun toolId(index: Int): String? {
        val ptr = lib.krate_tool_id(index) ?: return null
        val str = ptr.getString(0, "UTF-8")
        lib.krate_free(ptr)
        return str
    }

    fun toolName(index: Int): String? {
        val ptr = lib.krate_tool_name(index) ?: return null
        val str = ptr.getString(0, "UTF-8")
        lib.krate_free(ptr)
        return str
    }

    /** Password with explicit character classes; null if the length or class set is invalid. */
    fun password(length: Int, upper: Boolean, lower: Boolean, digits: Boolean, symbols: Boolean): String? {
        val ptr = lib.krate_password(length.toLong(), upper, lower, digits, symbols) ?: return null
        val str = ptr.getString(0, "UTF-8")
        lib.krate_free(ptr)
        return str
    }

    /** Raw category key ("Image", "Date", ...). Use this to branch; never the localized name. */
    fun toolCategory(index: Int): String? {
        val ptr = lib.krate_tool_category(index) ?: return null
        val str = ptr.getString(0, "UTF-8")
        lib.krate_free(ptr)
        return str
    }

    /** Localized category name, for display only. */
    fun toolCategoryName(index: Int): String? {
        val ptr = lib.krate_tool_category_name(index) ?: return null
        val str = ptr.getString(0, "UTF-8")
        lib.krate_free(ptr)
        return str
    }

    fun currencyStoreRates(base: String, json: String): Boolean {
        return lib.krate_currency_store_rates(base, json) == 1
    }
}
