import Foundation

public enum CursorPasswordLogin {
    public static let loginURL = "https://cursor.com/login"
    public static let timeoutSeconds = 180
    static let emailRe = try! NSRegularExpression(pattern: #"^[^@\s]+@[^@\s]+\.[^@\s]+$"#)

    public static func looksLikeEmail(_ value: String?) -> Bool {
        let text = (value ?? "").trimmingCharacters(in: .whitespaces)
        guard let at = text.firstIndex(of: "@") else { return false }
        return at > text.startIndex && at < text.index(before: text.endIndex) && !text.contains(" ")
    }

    public static func sanitizeEmail(_ raw: String?) -> String {
        let text = (raw ?? "").trimmingCharacters(in: .whitespaces).lowercased()
        if !looksLikeEmail(text) { return "" }
        let range = NSRange(text.startIndex..., in: text)
        if emailRe.firstMatch(in: text, range: range) == nil { return "" }
        return text
    }

    public static func defaultLabel(_ email: String, existing: String = "") -> String {
        let current = existing.trimmingCharacters(in: .whitespaces)
        if !current.isEmpty { return current }
        return sanitizeEmail(email)
    }

    public static func tokenFromCookies(_ cookies: [(name: String, value: String)]) -> String? {
        for item in cookies {
            if item.name.caseInsensitiveCompare(Token.cookieName) != .orderedSame { continue }
            let raw = item.value.trimmingCharacters(in: .whitespaces)
            if raw.isEmpty { return "" }
            return (try? Token.normalize(raw)) ?? raw
        }
        return ""
    }

    public static func autofillScript(email: String, password: String) -> String {
        let payloadObj: [String: String] = [
            "email": sanitizeEmail(email),
            "password": password,
        ]
        let data = (try? JSONSerialization.data(withJSONObject: payloadObj, options: [])) ?? Data("{}".utf8)
        let payload = String(data: data, encoding: .utf8) ?? "{}"
        return
            "(function(){" +
            "const c=\(payload);" +
            "function setNative(el,val){" +
            "if(!el)return;" +
            "const proto=el.tagName==='TEXTAREA'?window.HTMLTextAreaElement.prototype:window.HTMLInputElement.prototype;" +
            "const desc=Object.getOwnPropertyDescriptor(proto,'value');" +
            "if(desc&&desc.set)desc.set.call(el,val);else el.value=val;" +
            "el.dispatchEvent(new Event('input',{bubbles:true}));" +
            "el.dispatchEvent(new Event('change',{bubbles:true}));" +
            "}" +
            "if(document.querySelector('iframe[src*=\"challenges.cloudflare\"],iframe[src*=\"turnstile\"],input[autocomplete=\"one-time-code\"]'))" +
            "return 'need-user';" +
            "const emailEl=document.querySelector('input[type=\"email\"],input[name=\"email\"],input[autocomplete=\"username\"],input[autocomplete=\"email\"]');" +
            "if(emailEl&&c.email)setNative(emailEl,c.email);" +
            "const passEl=document.querySelector('input[type=\"password\"]');" +
            "if(passEl&&c.password)setNative(passEl,c.password);" +
            "const buttons=[...document.querySelectorAll('button,[type=submit]')];" +
            "const go=buttons.find(b=>/continue|sign in|log in|登录|继续/i.test((b.innerText||b.value||'')));" +
            "if(go&&!window.__cttClicked){window.__cttClicked=Date.now();go.click();return 'clicked';}" +
            "return passEl&&c.password?'filled':'waiting';" +
            "})();"
    }
}
