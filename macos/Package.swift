// swift-tools-version: 5.9
import PackageDescription

// Cargo also emits libcursor_core.dylib. `-lcursor_core` prefers that dylib,
// and its install name is the absolute path on the build machine, so the
// packaged app aborts in dyld anywhere else. Force the static archive instead.
let rustStaticLib: String = {
    let packageDir = "/" + #filePath.split(separator: "/").dropLast().joined(separator: "/")
    let repoRoot = "/" + packageDir.split(separator: "/").dropLast().joined(separator: "/")
    return repoRoot + "/core/target/release/libcursor_core.a"
}()

var targets: [Target] = [
    .target(
        name: "CursorTokenCore",
        path: "Sources/CursorTokenCore",
        linkerSettings: [
            .linkedLibrary("sqlite3"),
            .linkedFramework("Security", .when(platforms: [.macOS])),
            .unsafeFlags(["-force_load", rustStaticLib], .when(platforms: [.macOS])),
        ]
    ),
    .testTarget(
        name: "CursorTokenCoreTests",
        dependencies: ["CursorTokenCore"],
        path: "Tests/CursorTokenCoreTests",
        linkerSettings: [
            .linkedLibrary("sqlite3"),
        ]
    ),
]

#if os(macOS)
targets.append(
    .executableTarget(
        name: "CursorRemain",
        dependencies: ["CursorTokenCore"],
        path: "Sources/CursorRemain"
    )
)
#endif

var products: [Product] = [
    .library(name: "CursorTokenCore", targets: ["CursorTokenCore"]),
]
#if os(macOS)
products.append(.executable(name: "CursorRemain", targets: ["CursorRemain"]))
#endif

let package = Package(
    name: "CursorRemain",
    platforms: [.macOS(.v13)],
    products: products,
    targets: targets
)
