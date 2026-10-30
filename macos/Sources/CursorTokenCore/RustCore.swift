import Darwin
import Foundation

enum RustCore {
    static var lastText = ""
    // lastText keeps the raw FFI text because JSON decoding drops a leading BOM.
    static func call(_ op: String, _ input: Any) -> Any {
        let data: Data
        do {
            data = try JSONSerialization.data(withJSONObject: input, options: [.fragmentsAllowed])
        } catch {
            fatalError("cursor_core 输入无法编码: \(error)")
        }
        let json = String(data: data, encoding: .utf8) ?? "null"
        let ptr = op.withCString { opPtr in
            json.withCString { inputPtr in
                cr_call(opPtr, inputPtr)
            }
        }
        guard let ptr else { fatalError("cursor_core 没有返回结果") }
        defer { cr_free(ptr) }
        let text = String(cString: ptr)
        Self.lastText = text
        guard let output = text.data(using: .utf8),
              let value = try? JSONSerialization.jsonObject(with: output, options: [.fragmentsAllowed])
        else {
            fatalError("cursor_core 返回了非 JSON")
        }
        return value
    }

    static func string(_ op: String, _ input: Any) -> String {
        call(op, input) as? String ?? ""
    }

    static func intValue(_ op: String, _ input: Any) -> Int {
        let value = call(op, input)
        if let number = value as? Int { return number }
        if let number = value as? NSNumber { return number.intValue }
        return 0
    }

    static func doubleValue(_ op: String, _ input: Any) -> Double? {
        let value = call(op, input)
        if value is NSNull || value == nil { return nil }
        if let number = value as? Double { return number }
        if let number = value as? NSNumber { return number.doubleValue }
        return nil
    }

    static func bool(_ op: String, _ input: Any) -> Bool {
        call(op, input) as? Bool ?? false
    }

    static func strings(_ op: String, _ input: Any) -> [String] {
        (call(op, input) as? [String]) ?? []
    }

    static func object(_ op: String, _ input: Any) -> [String: Any] {
        call(op, input) as? [String: Any] ?? [:]
    }
}

private func cr_call(_ op: UnsafePointer<CChar>, _ input: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>? {
    // The symbol comes from libcursor_core.a. SPM cannot import a C function
    // without a module map, so the call is resolved through dlsym.
    guard let handle = dlopen(nil, RTLD_NOW), let symbol = dlsym(handle, "cr_call") else {
        fatalError("找不到 cursor_core 的 cr_call。先执行 cargo build --release --manifest-path core/Cargo.toml。")
    }
    typealias Call = @convention(c) (UnsafePointer<CChar>, UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>?
    return unsafeBitCast(symbol, to: Call.self)(op, input)
}

private func cr_free(_ ptr: UnsafeMutablePointer<CChar>) {
    guard let handle = dlopen(nil, RTLD_NOW), let symbol = dlsym(handle, "cr_free") else { return }
    typealias Free = @convention(c) (UnsafeMutablePointer<CChar>?) -> Void
    unsafeBitCast(symbol, to: Free.self)(ptr)
}
