import Foundation

public enum SettingsStamp {
    public static func parse(_ raw: String?) -> Date? {
        guard let raw = raw?.trimmingCharacters(in: .whitespacesAndNewlines), !raw.isEmpty else { return nil }
        guard let obj = RustCore.call("parse_settings_stamp", ["raw": raw]) as? [String: Any],
              let y = obj["year"] as? Int,
              let m = obj["month"] as? Int,
              let d = obj["day"] as? Int,
              let h = obj["hour"] as? Int,
              let min = obj["minute"] as? Int,
              let sec = obj["second"] as? Int
        else { return nil }
        var comps = DateComponents()
        comps.year = y
        comps.month = m
        comps.day = d
        comps.hour = h
        comps.minute = min
        comps.second = sec
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = .current
        return cal.date(from: comps)
    }
}
