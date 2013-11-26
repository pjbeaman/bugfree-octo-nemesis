bugfree-octo-nemesis
====================

Web tests for Mono.

Prerequisites
-------------

You need a server with the following software:

* Apache
* Samba
* Squid 2.7 (do not use Squid3 on SLED 11, it does not work, use the 'squid' package instead)
* A valid SSL certificate

The following installation instructions are for a SuSE-Linux based server - I'm running SLES 11
in an AWS-VPC micro instance.

Samba
-----

This is pretty much straightforward.  You need samba and samba-winbind.  Winbind is required for the
`/usr/bin/ntlm_auth` helper.  Create a test user, then check with

    /usr/bin/ntlm_auth --username=test

Squid
-----

You need Squid 2.7 - on SuSE, a simple `zypper install squid` will do.

Use the `/usr/bin/ntlm_auth` that comes with samba-winbind, not the one that's included in squid!

This is what I'm using:

    auth_param ntlm program /usr/bin/ntlm_auth --helper-protocol=squid-2.5-ntlmssp
    auth_param ntlm children 1
    auth_param ntlm keep_alive off
    acl authenticated proxy_auth REQUIRED

Unfortunately, squid will eat all debugging output from the helper process, but you can do a little trick
here; create /etc/squid/ntlm-auth-wrapper.sh:

    #!/bin/sh
    /usr/bin/ntlm_auth -E -d 9 --helper-protocol=squid-2.5-ntlmssp 2>/var/log/squid/ntlm-auth.log

To debug auth problems:

    debug_options ALL,1 33,9 28,9

Require auth:

    http_access allow all authenticated

SSL:

    http_port 3128
    https_port 3129 cert=/etc/apache2/cert/server.crt key=/etc/apache2/cert/server.key

If you want to configure it as a proxy only:

    cache deny all
    
Apache
------

Setup both http and https, using the same relative path - for instance `http://yourserver.com/web-tests/`
and `https://yourserver.com/web-tests/`, both pointing to `/data/www/web-tests` on the server.

Configure
---------

Copy `default.yml` to `yourserver.yml` and edit:

    yourserver:
      web_root: /data/www/web-tests
      web_host: yourserver.com
      web_prefix: /web-tests
      squid_address: http://yourserver:3128/
      squid_address_ssl: https://yourserver:3129/
      squid_user: username
      squid_pass: password

then

    $ cd /data/www/web-tests
    $ export BUGFREE_OCTO_NEMESIS=yourserver:yourserver.yml
    $ ./scripts/build.rb

Client-side config
------------------

The `./scripts/build.rb` than you ran on the server also created an `app.config` file; copy it to the
client, then you don't need to run the script there.

Launch Xamarin Studio to build the project.

