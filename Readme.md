# SimpleCoinTrading

SimpleCoinTrading은 가상자산(Coin) 알고리즘 트레이딩 시스템의 구조를 학습하고 구축하기 위한 샘플 프로젝트입니다. .NET 환경에서 gRPC 서버-클라이언트 아키텍처를 기반으로 하며, 핵심 로직 추상화와 실제 거래소 연동(빗썸) 및 가상 거래 기능을 포함하고 있습니다.

## 주요 특징

- **gRPC 기반 아키텍처**: 서버(Core/Infrastructure)와 클라이언트(WPF) 간의 효율적인 통신 및 실시간 이벤트 스트리밍.
- **알고리즘 엔진**: 다중 알고리즘을 독립적으로 관리하고 실행할 수 있는 유연한 구조.
- **브로커 추상화**: 실제 거래소(Bithumb)와 가상 거래소(PaperBroker)를 동일한 인터페이스로 처리.
- **시간 제어(Time Flow)**: 실시간 모드뿐만 아니라 과거 데이터를 활용한 리플레이 및 백테스트 환경 지원 (`VirtualClock`).
- **상태 관리**: 주문 상태, 체결 내역, 포지션 정보 등을 실시간으로 프로젝션하고 동기화.
- **안전 장치(Kill Switch)**: 비상 상황 시 모든 주문을 취소하고 알고리즘을 중단할 수 있는 기능.

## 프로젝트 구조

```text
SimpleCoinTrading
├── SimpleCoinTrading.Core           # 핵심 도메인 로직 (알고리즘 엔진, 브로커 인터페이스, 주문/데이터 처리)
├── SimpleCoinTrading.Infrastructure   # 외부 연동 및 인프라 (빗썸 WebSocket, CSV 리플레이)
├── SimpleCoinTrading.Server         # gRPC 서버 프로젝트 (DI 설정, API 엔드포인트)
├── SimpleCoinTrading.Wpf            # WPF 기반 관리 클라이언트 (상태 모니터링, 알고리즘 제어)
├── SimpleCoinTrading.Console        # CLI 테스트 도구
└── SimpleCoinTrading.Core.Tests     # 단위 테스트 및 통합 테스트
```

## 주요 컴포넌트

### Core
- `IAlgorithm`: 알고리즘 구현을 위한 표준 인터페이스.
- `AlgorithmEngine`: 알고리즘의 생명주기(Setup, Start, Stop) 관리.
- `IBroker`: 거래소 연동을 위한 추상 계층.
- `MarketPipeline`: 마켓 데이터 수집 및 전파.
- `OrderOrchestrator`: 주문의 생성, 검증 및 실행 조정.

### Infrastructure
- `BithumbWebSocketMarketDataSource`: 빗썸 WebSocket API를 통한 실시간 시세 수신.
- `PaperBroker`: 실거래 없이 전략을 테스트할 수 있는 가상 매매 시뮬레이터.

### Server
- `TradingControlGrpcService`: 주문 및 전체 시스템 상태 제어 서비스.
- `AlgorithmAdminGrpcService`: 알고리즘 목록 조회 및 시작/정지 제어 서비스.
- `AlgoLogGrpcService`: 알고리즘 로그 실시간 스트리밍 서비스.

## 시작하기

### 사전 요구 사항
- .NET 10.0 SDK 이상
- (선택 사항) Docker (가상 환경 구축 시)

### 실행 방법

1. **서버 실행**
   ```bash
   cd SimpleCoinTrading.Server
   dotnet run
   ```
   서버는 기본적으로 `http://localhost:5200`에서 gRPC 서비스를 시작합니다.

2. **클라이언트 실행**
   ```bash
   cd SimpleCoinTrading.Wpf
   dotnet run
   ```
   WPF 앱을 통해 실시간 주문 현황, 알고리즘 상태를 확인하고 제어할 수 있습니다.

## 샘플 알고리즘
- **VolatilityBreakoutAlgorithm**: 래리 윌리엄스의 변동성 돌파 전략을 구현한 샘플입니다. (전일 레인지 * K값)을 돌파할 때 매수하고 장 마감 시 매도하는 로직을 포함하고 있습니다.

## 면책 조항 (Disclaimer)
이 프로젝트는 교육 및 샘플 코드 작성을 목적으로 제작되었습니다. 실제 투자에 사용하기 위해서는 충분한 테스트와 검증이 필요하며, 이 코드를 사용하여 발생하는 어떠한 손실에 대해서도 책임을 지지 않습니다.
