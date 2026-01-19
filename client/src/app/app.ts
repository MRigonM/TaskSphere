import {Component, computed, signal} from '@angular/core';
import {RouterOutlet} from '@angular/router';
import {FooterComponent} from './layout/footer/footer.component';
import {HeaderComponent} from './layout/header/header.component';
import {SubHeaderComponent} from './layout/sub-header/sub-header.component';
import {NgIf} from '@angular/common';
import {AuthStoreService} from './core/services/auth-store.service';


@Component({
  selector: 'app-root',
  imports: [RouterOutlet, FooterComponent, HeaderComponent, SubHeaderComponent, NgIf],
  templateUrl: './app.html',
  styleUrl: './app.css',
  standalone: true
})
export class App {

  constructor(private auth: AuthStoreService) {}
  year = new Date().getFullYear();
  isLoggedIn = computed(() => this.auth.isLoggedIn());
}
