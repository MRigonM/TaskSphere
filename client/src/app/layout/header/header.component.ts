import { Component } from '@angular/core';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter } from 'rxjs';
import {AuthStoreService} from '../../core/services/auth-store.service';
import {NgIf} from '@angular/common';

@Component({
  selector: 'app-header',
  standalone: true,
  imports: [RouterLink, NgIf],
  templateUrl: './header.component.html',
})
export class HeaderComponent {
  name: string | null = null;
  menuOpen = false;

  constructor(public auth: AuthStoreService, private router: Router) {
    this.refreshName();
    this.router.events.pipe(filter((e) => e instanceof NavigationEnd)).subscribe(() => this.refreshName());
  }

  refreshName() {
    this.name = this.auth.getName();
  }

  logout() {
    this.auth.clear();
    this.refreshName();
    this.router.navigateByUrl('/');
  }

  toggleMenu() {
    this.menuOpen = !this.menuOpen;
  }

  closeMenu() {
    this.menuOpen = false;
  }
}
