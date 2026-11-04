// Angular's partially compiled packages (@angular/common/http) link their declarations at load time
// through the JIT compiler facade, so it must be loaded before any spec imports them.
import '@angular/compiler';
