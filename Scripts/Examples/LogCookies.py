# --------------------------------------------------------------------------------
# WwwProxy\Scripts\Examples\Cookies.py
#
# Copyright ©2008 Liam Kirton <liam@int3.ws>
# --------------------------------------------------------------------------------
# Cookies.py
#
# Created: 10/04/2008
# --------------------------------------------------------------------------------

# Parameter Types

# WwwProxy.ProxyRequest
# .Id
# .Pass
# .Skip
# .Header
# .Data

# WwwProxy.ProxyResponse
# .Id
# .Completable
# .Pass
# .Skip
# .Header
# .Contents
	
# --------------------------------------------------------------------------------

import clr
clr.AddReference('WwwProxy')
from WwwProxy import *

import re

# --------------------------------------------------------------------------------

class WwwProxyFilter(object):

	# ----------------------------------------------------------------------------
	
	def __init__(self):
		pass

	# ----------------------------------------------------------------------------
	
	def pre_request_filter(self, request):
		pass

	# ----------------------------------------------------------------------------

	def post_request_filter(self, request):
		pass
	
	# ----------------------------------------------------------------------------
		
	def pre_response_filter(self, request, response):
		# Extract url from request header
		request_match = re.compile('^[A-Z]+\\s+(.*)\\s+HTTP/\\d\\.\\d').match(request.Header)
		if request_match != None:
			url = request_match.groups()[0]
			
			# Extract host from request header
			host_search = re.compile('Host\\:\\s+([\\S+\\.]+)[\r\n]*', re.I | re.M).search(request.Header)
			if host_search != None:
				host = host_search.groups()[0]
				
				# Filter specific domain
				#if not re.search('google.co.uk', host):
				#	return
				
				# Extract Set-Cookie from response header
				set_cookie_search = re.compile('Set-Cookie\\:\\s+([^\r\n]*)[\r\n]*', re.I | re.M).search(response.Header)
				if set_cookie_search != None:
					cookie = set_cookie_search.groups()[0]
					
					# Append result to Cookies.txt
					output = '\"' + host + '\" \"' + \
							 url + '\" Set-Cookie: \"' + \
							 cookie + '\"\n'
					f = open('Cookies.txt', 'a')
					f.write(output)
					f.close()
	
	# ----------------------------------------------------------------------------
	
	def post_response_filter(self, request, response):
		pass
	
	# ----------------------------------------------------------------------------

# --------------------------------------------------------------------------------
