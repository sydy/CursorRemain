import Foundation
import XCTest
@testable import CursorTokenCore

#if canImport(Security)
import Security
#endif

final class TokenProtectorTests: XCTestCase {
    func testClassifyReadTreatsOnlyMissingAsSafeToMint() {
        #if canImport(Security)
        XCTAssertEqual(TokenProtector.classifyRead(errSecSuccess), .found)
        XCTAssertEqual(TokenProtector.classifyRead(errSecItemNotFound), .missing)
        XCTAssertEqual(TokenProtector.classifyRead(errSecUserCanceled), .unavailable)
        XCTAssertEqual(TokenProtector.classifyRead(errSecAuthFailed), .unavailable)
        XCTAssertEqual(TokenProtector.classifyRead(errSecInteractionNotAllowed), .unavailable)
        XCTAssertEqual(TokenProtector.classifyRead(errSecWrPerm), .unavailable)
        XCTAssertTrue(TokenProtector.shouldMintNewKey(current: .missing, legacy: .missing))
        XCTAssertFalse(TokenProtector.shouldMintNewKey(current: .unavailable, legacy: .missing))
        XCTAssertFalse(TokenProtector.shouldMintNewKey(current: .missing, legacy: .unavailable))
        XCTAssertFalse(TokenProtector.shouldMintNewKey(current: .found, legacy: .missing))
        #endif
    }

    func testOpenAccessDoesNotBindASpecificApp() throws {
        #if canImport(Security)
        guard let access = TokenProtector.makeOpenAccess() else {
            XCTFail("makeOpenAccess should create a file-based keychain ACL")
            return
        }
        var list: CFArray?
        XCTAssertEqual(SecAccessCopyACLList(access, &list), errSecSuccess)
        let acls = try XCTUnwrap(list as? [SecACL])
        XCTAssertFalse(acls.isEmpty)
        var openACLCount = 0
        for acl in acls {
            var apps: CFArray?
            var descriptor: CFString?
            var selector = SecKeychainPromptSelector()
            XCTAssertEqual(SecACLCopyContents(acl, &apps, &descriptor, &selector), errSecSuccess)
            let count = (apps as? [Any])?.count ?? 0
            if count == 0 { openACLCount += 1 }
        }
        XCTAssertGreaterThan(openACLCount, 0, "ACL must not pin a CDHash, or auto-update will prompt again")
        #endif
    }

    func testWrapKeyRoundtripUsesStableAccount() throws {
        #if canImport(CryptoKit) && canImport(Security)
        let token = "user_01KEY%3A%3Aaaa.bbb.ccc"
        let blob = try TokenProtector.protect(token)
        XCTAssertTrue(TokenProtector.isProtected(blob))
        XCTAssertEqual(TokenProtector.unprotect(blob), token)
        XCTAssertEqual(TokenProtector.keyAccount, "wrap-key-v2")
        XCTAssertEqual(TokenProtector.legacyKeyAccount, "wrap-key-v1")
        #endif
    }
}
