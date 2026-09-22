# ScrollDownButton

PCF field control tối giản: một nút `↓` neo ở góc dưới phải. Khi bấm, control tìm
container có scrollbar gần nhất và cuộn xuống cuối. Bind control vào một text
column bất kỳ; control không đọc hoặc sửa dữ liệu của column đó.

```powershell
cd .\dataverse\pcf\ScrollDownButton
npm ci
npm run lint
npm run build -- --buildMode production
```

Việc tìm scroll container đi qua DOM của host để đáp ứng hành vi yêu cầu, nên cần
test lại sau khi bind vào form/model-driven app thực tế; cấu trúc DOM host có thể
thay đổi giữa các phiên bản Power Apps.

## Triển khai Developer

Ngày 2026-09-22, `fmc_FMC.Bms.ScrollDownButton` version `1.0.0` đã được push vào
solution `FMCentralBms`. Binding thử nghiệm trên form `BMS Point - Demo` sau đó
đã được gỡ vì yêu cầu cuối là đặt nút ở Home. Home là generative page nên nút
`↓` đang được triển khai trực tiếp trong source React của page, không host field
PCF này. Control vẫn nằm trong solution để có thể tái sử dụng, nhưng hiện không
bind vào form `fmc_bmspoint`.

Script triển khai/kiểm tra lặp lại:

```powershell
.\scripts\Deploy-ScrollDownButton.ps1 -Mode Verify
```

Kết quả `FormBindingPresent: false` xác nhận binding trên form đã được gỡ.
