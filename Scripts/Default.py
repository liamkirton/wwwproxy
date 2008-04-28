# --------------------------------------------------------------------------------
# WwwProxy\Scripts\Default.py
#
# Copyright ©2008 Liam Kirton <liam@int3.ws>
# --------------------------------------------------------------------------------
# Default.py
#
# Created: 09/04/2008
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
		request_match = re.compile(r'^([A-Z]+)\s+(.*)\s+HTTP/\d\.\d').match(request.Header)
		if request_match != None:
			# Print request
			request_groups = request_match.groups()
			print '>>>>> pre_request_filter(%d, %s, %s)' % (request.Id, request_groups[0], request_groups[1])
			print '\n%s\n' % request.Header
			
			if request.Data != None:
				if re.compile(r'.+=.+&*').match(request.Data) != None:
					# Parse, modify and rebuild POST data
					new_request_data = ''
					for s in re.compile(r'&', re.I | re.M).split(request.Data):
						p,v = s.split('=')
						print '\"%s\"=\"%s\"' % (p, v)
						
						# !!! Modify POST "p=v" pairs here !!!
						
						if len(new_request_data) != 0:
							new_request_data += '&'
						new_request_data += p + '=' + v
					
					print ''
					request.Data = new_request_data
				else:
					print '%s\n' % request.Data
			
			# Remove non-essential headers
			remove_headers = ['Accept', 'Accept-Language', 'UA-CPU']
			
			# Optional: Remove cache-related headers (force re-download)
			remove_headers = remove_headers + ['Cache-Control', 'If-Modified-Since', 'Pragma']
			
			for h in remove_headers:
				request.Header = re.compile(h + '\\:\\s+.*[\r\n]*', re.I | re.M).sub('', request.Header)
			
			# Replace User-Agent
			user_agent_re = re.compile('(User-Agent\\:\\s+)(.*)([\r\n]*)', re.I | re.M)
			old_user_agent_search = user_agent_re.search(request.Header)
			if old_user_agent_search != None:
				old_user_agent = old_user_agent_search.groups()[1]
				
				# !!! Modify User-Agent here !!!
				new_user_agent = 'Mozilla/4.0 (compatible; WwwProxy 1.2.2.1 (http://int3.ws/); WwwProxyScripting.dll; IronPython)'
				
				new_user_agent_sub = r'\g<1>' + new_user_agent
				if len(old_user_agent_search.groups()[2]) != 0:
					new_user_agent_sub += r'\g<3>'
				request.Header = user_agent_re.sub(new_user_agent_sub, request.Header)
			
			# Replace Cookie
			cookie_re = re.compile('(Cookie\\:\\s+)(.*)([\r\n]*)', re.I | re.M)
			old_cookie_search = cookie_re.search(request.Header)
			if old_cookie_search != None:
				old_cookie = old_cookie_search.groups()[1]
				
				# !!! Modify Cookie header here !!!
				new_cookie = old_cookie
				
				new_cookie_sub = r'\g<1>' + new_cookie
				if len(old_cookie_search.groups()[2]) != 0:
					new_cookie_sub += r'\g<3>'
				request.Header = cookie_re.sub(new_cookie_sub, request.Header)
			
			# Automatically pass uninteresting requests, without notifying chained
			# event handlers
			uninteresting_request_types = ['.jpg', '.gif', '.png', '.js', '.css']
			request_type = request_groups[1].split('?')[0]
			for s in uninteresting_request_types:
				if request_type.endswith(s):
					request.Pass = True
					request.Skip = True
					break
						
		if request.Header[len(request.Header) - 2:] == '\r\n':
			request.Header = request.Header[:len(request.Header) - 2]

	# ----------------------------------------------------------------------------
	
	def post_request_filter(self, request):
		request_match = re.compile(r'^([A-Z]+)\s+(.*)\s+HTTP/\d\.\d').match(request.Header)
		if request_match != None:
			request_groups = request_match.groups()
			print '>>>>> post_request_filter(%d, %s, %s)\n' % (request.Id, request_groups[0], request_groups[1])
		
	# ----------------------------------------------------------------------------
			
	def pre_response_filter(self, request, response):
		request_match = re.compile(r'^([A-Z]+)\s+(.*)\s+HTTP/\d\.\d').match(request.Header)
		if request_match != None:
			# Print request/response
			request_groups = request_match.groups()
			print '<<<<< pre_response_filter(%d, Completable=%s, %s, %s)' % (request.Id, response.Completable, request_groups[0], request_groups[1])
			print '\n%s\n' % response.Header
			
			# Remove non-essential headers
			remove_headers = ['Via', 'X-Cache']
			
			# Optional: Remove cache-related headers
			remove_headers = remove_headers + ['Age', 'Cache-Control', 'Date', 'Expires', 'Last-Modified']
			
			for h in remove_headers:
				response.Header = re.compile(h + '\\:\\s+.*[\r\n]*', re.I | re.M).sub('', response.Header)
				
		if response.Header[len(response.Header) - 2:] == '\r\n':
			response.Header = response.Header[:len(response.Header) - 2]

	# ----------------------------------------------------------------------------
	
	def post_response_filter(self, request, response):
		request_match = re.compile(r'^([A-Z]+)\s+(.*)\s+HTTP/\d\.\d').match(request.Header)
		if request_match != None:
			# Print request/response
			request_groups = request_match.groups()
			print '<<<<< post_response_filter(%d, Completable=%s, %s, %s)\n' % (request.Id, response.Completable, request_groups[0], request_groups[1])
	
	# ----------------------------------------------------------------------------

# --------------------------------------------------------------------------------
