// swift-tools-version: 5.9
import PackageDescription

var targets: [Target] = [
    .target(
        name: "CursorTokenCore",
        path: "Sources/CursorTokenCore",
        linkerSettings: [
            .linkedLibrary("sqlite3"),
            .linkedFramework("Security", .when(platforms: [.macOS])),
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
