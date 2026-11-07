// swift-tools-version: 5.9
import Foundation
import PackageDescription

// Force-load the Rust static archive. Cargo also emits libcursor_core.dylib;
// plain `-lcursor_core` prefers that dylib and embeds the build-machine
// absolute install name, so Release zips crash on every other Mac.
let packageDir = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
let cursorCoreStatic = packageDir
    .appendingPathComponent("../core/target/release/libcursor_core.a")
    .standardizedFileURL
    .path

var targets: [Target] = [
    .target(
        name: "CursorTokenCore",
        path: "Sources/CursorTokenCore",
        linkerSettings: [
            .linkedLibrary("sqlite3"),
            .linkedFramework("Security", .when(platforms: [.macOS])),
            .unsafeFlags(["-force_load", cursorCoreStatic], .when(platforms: [.macOS])),
        ]
    ),
    .testTarget(
        name: "CursorTokenCoreTests",
        dependencies: ["CursorTokenCore"],
        path: "Tests/CursorTokenCoreTests",
        linkerSettings: [
            .linkedLibrary("sqlite3"),
            .linkedFramework("Security", .when(platforms: [.macOS])),
            .unsafeFlags(["-force_load", cursorCoreStatic], .when(platforms: [.macOS])),
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
