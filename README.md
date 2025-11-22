# Unity Client Framework

개인 프로젝트 및 실무에서 사용하기 위해 구축한 유니티 클라이언트 프레임워크입니다.
**리소스 관리(Addressables)**, **네트워크(HTTP)**, **데이터 핸들링(JSON/Table)**을 위한 코어 모듈로 구성되어 있습니다.

## 📂 Project Structure

```text
Assets/
├── Addressable/       # Addressables 기반 리소스 관리 및 에디터 툴
├── Data/              # 로컬/서버 데이터 동기화 및 정적 테이블(TSV) 처리
├── Network/           # UnityWebRequest 래퍼 및 통신 암호화
├── KeyReference/      # 참조 카운팅 유틸리티
└── Script/
    └── Common/        # Singleton 등 공통 유틸리티

1. Resource Management (Addressable)
Unity Addressables 시스템을 래핑하여 참조 카운팅(Reference Counting) 및 자동 해제 기능을 구현했습니다.

주요 기능
AddressablesManager: 리소스 로드/해제 요청을 중앙에서 관리합니다. 동일 리소스 중복 로드를 방지하고, 참조 카운트가 0이 될 때 언로드(Unload)합니다.

AddressableScope: IDisposable 패턴을 사용하여 using 구문이나 컴포넌트 수명(OnDestroy)에 맞춰 리소스를 자동으로 해제합니다.

In-flight Cancellation: 비동기 로딩 중 취소 요청 시 CancellationToken을 통해 작업을 중단하여 리소스 낭비를 방지합니다.

Editor Tools:

AddressableKeySetter: 폴더 경로를 기반으로 Addressable Key를 자동 생성합니다.

AddressableFolderGrouper: 폴더 구조에 따라 Group 및 Schema(Local/Remote)를 자동 설정합니다.

사용 예시 (Code Snippet)
// Scope를 이용한 자동 리소스 해제 예시
public async UniTask LoadCharacter(string key)
{
    // 이 컴포넌트가 파괴될 때 등록된 리소스 자동 해제
    var scope = this.GetAddressableScope(); 
    
    var prefab = await scope.LoadAsync<GameObject>(key);
    var instance = Instantiate(prefab);
    
    // 인스턴스도 스코프에 등록하여 함께 파괴되도록 설정
    scope.TrackInstance(instance); 
}

2. Network Layer
UnityWebRequest를 기반으로 한 HTTP 통신 모듈로, 암호화 및 요청 큐(Queue) 관리를 지원합니다.

주요 기능
WebRequestAPI: API 요청 단위를 캡슐화한 클래스입니다. 파라미터 직렬화 및 타임아웃 처리를 담당합니다.

Security: 패킷 전송 시 AES 암호화 및 Base64 인코딩을 적용하여 데이터 무결성을 보호합니다.

Request Batching: WebRequestBatch를 통해 여러 API 요청을 병렬로 처리하고, 모든 응답을 기다리거나 실패 시 일괄 처리하는 기능을 지원합니다.

Retry & Queueing: 네트워크 불안정 시 요청을 큐에 보관하고, 연결 복구 시 재전송하는 로직을 포함합니다.

3. Data Handling
동적 유저 데이터(Server Data)와 정적 테이블 데이터(Table Data)를 처리하는 모듈입니다.

User Data (ServerData.cs)
Hybrid Synchronization: 서버 데이터를 로컬 객체에 동기화할 때, Import(덮어쓰기) 모드와 Update(병합) 모드를 지원합니다.

Smart Reflection: JsonDataBase 클래스가 리플렉션을 통해 JSON 필드와 C# 객체 필드를 매핑하며, List나 Dictionary 같은 컬렉션 타입의 변경 사항도 감지하여 병합합니다.

Secure Storage: 로컬 저장(PlayerPrefs) 시 AES 암호화를 강제 적용합니다.

Table Data (TableBase.cs)
TSV Parsing: 텍스트 기반의 TSV(Tab-Separated Values) 데이터를 파싱하여 메모리에 적재합니다.

Build Pipeline: 에디터 툴(CreateTable.cs)을 통해 원본 데이터를 암호화된 바이너리/텍스트 파일로 빌드합니다.

Generic Tables: IndexedTable<id, data>, KeyedTable<key, data> 등 다양한 자료구조를 지원하여 검색 성능을 최적화했습니다.

4. Utilities
KeyReferenceCounter
특정 기능(예: 로딩 인디케이터, UI 블로커)의 활성 상태를 카운팅 방식으로 관리합니다.

여러 소스에서 동시에 Enable을 호출하더라도, 모든 호출자가 Disable을 할 때까지 상태를 유지합니다.

Singleton / PersistentSingleton
제네릭 싱글톤 패턴을 구현하여 매니저 클래스의 접근성을 단순화했습니다.
